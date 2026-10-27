using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #580: cevap anahtarı okumasının ve tipe göre çözümün Claude yanıt şemaları (<c>GetResponseAsync&lt;T&gt;</c>) ve şema → kayıt
/// eşlemesi. Şemalarda aynı record iki özellikte kullanılmaz (MEAI ikinciyi <c>$ref</c> yapar, Anthropic reddeder;
/// <c>LabelingResponseSchemaTests</c> denetler). Çözüm şeması / eşleme / tip açıklaması değişirse tipin çözüm revizyonunu
/// (<see cref="ActivityStructureSettings"/> <c>SolutionTypeRevisions</c>), anahtar şeması değişirse <c>answerKey</c> revizyonunu artırın.
/// </summary>
internal static class ActivitySolutionDrafts
{
    public const int MaxEntriesPerPage = 300;

    // ------------------------------------------------------------------ cevap anahtarı

    internal sealed record AnswerKeyDraft(
        [property: Description("Every answer on the page, in reading order.")] List<AnswerKeyDraftEntry> Entries);

    internal sealed record AnswerKeyDraftEntry(
        [property: Description("Book page number of the activity, as printed in the key; 0 if none.")] int PrintedPage,
        [property: Description("Activity label as printed, or an empty string.")] string ActivityLabel,
        [property: Description("Item label inside the activity, or an empty string.")] string ItemLabel,
        [property: Description("The answer as printed, or an empty string.")] string AnswerText,
        [property: Description("True when the answer is (also) a picture.")] bool Visual,
        int X1, int Y1, int X2, int Y2);

    /// <summary>
    /// Claude'un maddeleri → kayıt. Etiket/cevap temizlenir ve kısaltılır; boş madde (etiketsiz, cevapsız, görsel değil) atılır;
    /// görsel maddenin kutusu sayfa pikseline çevrilir (kutu yoksa ya da çok küçükse madde görsel sayılmaz). En fazla
    /// <see cref="MaxEntriesPerPage"/> madde ve <see cref="LabelingAnswerKeyEntry.MaxVisualEntriesPerPage"/> görsel madde (fazlası metin
    /// olarak kalır; metni de yoksa atılır).
    /// </summary>
    public static IReadOnlyList<AnswerKeyReadEntry> Map(AnswerKeyDraft draft, EncodedRegion region, Size page)
    {
        var result = new List<AnswerKeyReadEntry>();
        var bounds = new Rectangle(Point.Empty, page);
        foreach (var e in draft.Entries ?? [])
        {
            if (e is null || result.Count >= MaxEntriesPerPage)
                continue;
            var activity = ActivityDrafts.Label(e.ActivityLabel, LabelingAnswerKeyEntry.MaxLabelLength);
            var item = ActivityDrafts.Label(e.ItemLabel, LabelingAnswerKeyEntry.MaxLabelLength);
            var answer = ActivityDrafts.Label(e.AnswerText, LabelingAnswerKeyEntry.MaxAnswerLength);
            Rectangle? box = null;
            if (e.Visual && result.Count(r => r.Visual) < LabelingAnswerKeyEntry.MaxVisualEntriesPerPage)
            {
                var rect = Rectangle.Intersect(region.ToPage(e.X1, e.Y1, e.X2, e.Y2), bounds);
                if (rect.Width >= 4 && rect.Height >= 4)
                    box = rect;
            }
            if (answer is null && box is null)
                continue;
            int? printed = e.PrintedPage is > 0 and <= BookPageMap.MaxFilePage ? e.PrintedPage : null;
            result.Add(new AnswerKeyReadEntry(printed, activity, item, answer, box is not null, box));
        }
        return result;
    }

    // ------------------------------------------------------------------ çözüm şemaları

    internal sealed record MatrixSolutionDraft(
        [property: Description("For every row of the structure, the correct column(s).")] List<MatrixSolutionRow> Rows);

    internal sealed record MatrixSolutionRow(
        [property: Description("Row id from the structure.")] string RowId,
        [property: Description("Ids of the correct columns.")] List<string> ColumnIds);

    internal sealed record SlotSolutionDraft(
        [property: Description("For every slot of the structure, its answer.")] List<SlotSolutionItem> Slots);

    internal sealed record SlotSolutionItem(
        [property: Description("Slot id from the structure.")] string SlotId,
        [property: Description("The answer (for select: the option id).")] string Answer,
        [property: Description("Other acceptable forms of the answer; may be empty.")] List<string> Alternatives);

    internal sealed record DragSolutionDraft(
        [property: Description("For every target of the structure, the cards that belong there.")] List<DragSolutionTarget> Targets);

    internal sealed record DragSolutionTarget(
        [property: Description("Target id from the structure.")] string TargetId,
        [property: Description("Ids of the cards placed on this target.")] List<string> CardIds);

    internal sealed record MatchSolutionDraft(
        [property: Description("Every pair to connect (examples excluded).")] List<MatchSolutionPair> Pairs);

    internal sealed record MatchSolutionPair(
        [property: Description("Left item id.")] string LeftId,
        [property: Description("Right item id.")] string RightId);

    /// <summary>Talimatın sonuna kodda eklenen tip açıklaması (sabit; tipin çözüm revizyonuna dahil).</summary>
    public static string TypeExplanation(string interactionType) => interactionType switch
    {
        MatrixChoiceGrader.Type =>
            "The activity is a table of rows and choice columns (matrixChoice). Return rows: for every row id of the structure, " +
            "columnIds = the id(s) of the correct column; exactly one unless multiPerRow is true.",
        SlotFillGrader.Type =>
            "The activity has blanks the student fills in (slotFill). Return slots: for every slot id of the structure, answer = " +
            "the correct answer. For a number slot write only the number (digits, a fraction such as 3/4 or a mixed number such " +
            "as 1 2/3), without its unit. For a select slot write the id of the correct option. For a palette slot write one of " +
            "its symbols. alternatives = other acceptable spellings (text slots only; otherwise an empty list).",
        DragPlaceGrader.Type =>
            "The student places cards on targets (dragPlace). Return targets: for every target id of the structure, cardIds = the " +
            "ids of the cards that belong there (at most its capacity; exactly one per target when mode is order; an empty list " +
            "for a target that stays empty). Cards that belong nowhere are distractors.",
        MatchGrader.Type =>
            "The student matches left items with right items (match). Return pairs: one pair for every left item that is not " +
            "already linked in prelinked, with leftId and the id of its right item; a right item is used at most once.",
        _ => throw new ArgumentOutOfRangeException(nameof(interactionType), interactionType, "Unsupported activity type."),
    };

    /// <summary>
    /// Prompt'a giden yapı: planın kutusuz kopyası (id, etiket, tip alanları). Kutular blok uzayında olduğu için gönderilmez;
    /// Claude öğeleri etiket ve sırayla görselde bulur.
    /// </summary>
    public static string Structure(string planJson)
    {
        var node = JsonNode.Parse(planJson) ?? new JsonObject();
        Strip(node);
        return node.ToJsonString();
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                obj.Remove("box");
                obj.Remove("cells");
                foreach (var (_, child) in obj.ToList())
                    Strip(child);
                break;
            case JsonArray arr:
                foreach (var child in arr)
                    Strip(child);
                break;
        }
    }

    // ------------------------------------------------------------------ eşleme (taslak id'leri)

    /// <summary>
    /// Matris: plandaki satır/kolon id'leri dışındakiler atılır, kolonlar tekilleştirilir. Kapsama/tek-seçim kuralını kontrolcünün
    /// <c>ValidateDefinition</c>'ı değerlendirir (eksik satır → geçersiz taslak, öğretmen düzeltir).
    /// </summary>
    public static JsonObject Map(MatrixSolutionDraft draft, JsonObject plan)
    {
        var rows = Ids(plan["rows"]);
        var columns = Ids(plan["columns"]);
        var solution = new JsonObject();
        foreach (var row in draft.Rows ?? [])
        {
            var rowId = row?.RowId?.Trim();
            if (rowId is null || !rows.Contains(rowId) || solution.ContainsKey(rowId))
                continue;
            var selected = new JsonArray();
            foreach (var col in (row!.ColumnIds ?? []).Select(c => c?.Trim()).Where(c => c is not null && columns.Contains(c)).Distinct())
                selected.Add(col);
            solution[rowId] = selected;
        }
        return solution;
    }

    /// <summary>
    /// Kutu çözümü kutunun türüne göre: number → <c>{value}</c> (birim ipucu sondan atılır), text → <c>{accept}</c> (cevap +
    /// alternatifler, normalize hâline göre tekil, en çok 20), select → <c>{optionIds}</c> (id ya da etiket eşleşmesi), palette →
    /// <c>{accept}</c> (planın sembolleri içinden).
    /// </summary>
    public static JsonObject Map(SlotSolutionDraft draft, JsonObject plan)
    {
        var slots = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var slot in (plan["slots"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if ((string?)slot["id"] is { } id)
                slots.TryAdd(id, slot);
        }

        var solution = new JsonObject();
        foreach (var item in draft.Slots ?? [])
        {
            var slotId = item?.SlotId?.Trim();
            if (slotId is null || !slots.TryGetValue(slotId, out var slot) || solution.ContainsKey(slotId))
                continue;
            var answers = new[] { item!.Answer }.Concat(item.Alternatives ?? [])
                .Select(a => a?.Trim()).Where(a => !string.IsNullOrEmpty(a)).Select(a => a!).ToList();
            if (answers.Count == 0)
                continue;
            if (SlotSolution(slot, answers) is { } mapped)
                solution[slotId] = mapped;
        }
        return solution;
    }

    private static readonly Regex NumberPrefix = new(@"^[-+−]?\s*[\d.,]+(\s+\d+\s*/\s*\d+|\s*/\s*\d+)?", RegexOptions.CultureInvariant);

    private static JsonObject? SlotSolution(JsonObject slot, List<string> answers)
    {
        var input = slot["input"] as JsonObject;
        switch ((string?)input?["kind"])
        {
            case SlotFillGrader.KindNumber:
            {
                var unit = (string?)input!["unit"];
                foreach (var answer in answers)
                {
                    var candidate = answer;
                    if (!string.IsNullOrEmpty(unit) && candidate.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
                        candidate = candidate[..^unit.Length].Trim();
                    if (SlotAnswerNormalizer.TryParseNumber(candidate, out var parsed) && parsed.Unit is null)
                        return new JsonObject { ["value"] = candidate };
                    var prefix = NumberPrefix.Match(candidate);
                    if (prefix.Success && SlotAnswerNormalizer.TryParseNumber(prefix.Value.Trim(), out parsed) && parsed.Unit is null)
                        return new JsonObject { ["value"] = prefix.Value.Trim() };
                }
                return null;
            }
            case SlotFillGrader.KindSelect:
            {
                var options = (input!["options"] as JsonArray ?? []).OfType<JsonObject>()
                    .Select(o => (Id: (string?)o["id"], Label: (string?)o["label"]))
                    .Where(o => o.Id is not null)
                    .ToList();
                foreach (var answer in answers)
                {
                    var match = options.FirstOrDefault(o => string.Equals(o.Id, answer, StringComparison.Ordinal));
                    if (match.Id is null)
                        match = options.FirstOrDefault(o => o.Label is not null
                            && SlotAnswerNormalizer.NormalizeText(o.Label) == SlotAnswerNormalizer.NormalizeText(answer));
                    if (match.Id is not null)
                        return new JsonObject { ["optionIds"] = new JsonArray(match.Id) };
                }
                return null;
            }
            case SlotFillGrader.KindPalette:
            {
                var symbols = (input!["symbols"] as JsonArray ?? []).Select(s => (string?)s).Where(s => s is not null)
                    .Select(s => SlotAnswerNormalizer.NormalizePalette(s)).ToHashSet(StringComparer.Ordinal);
                var accept = Distinct(answers, SlotAnswerNormalizer.NormalizePalette)
                    .Where(a => symbols.Count == 0 || symbols.Contains(SlotAnswerNormalizer.NormalizePalette(a)))
                    .Take(SlotFillGrader.MaxAcceptEntries).ToList();
                return accept.Count == 0 ? null : new JsonObject { ["accept"] = ToArray(accept) };
            }
            default:
            {
                var accept = Distinct(answers, SlotAnswerNormalizer.NormalizeText)
                    .Where(a => a.Length <= SlotFillGrader.MaxAcceptLength)
                    .Take(SlotFillGrader.MaxAcceptEntries).ToList();
                return accept.Count == 0 ? null : new JsonObject { ["accept"] = ToArray(accept) };
            }
        }
    }

    /// <summary>Hedef → kartlar; plandaki her hedef çözümde yer alır (verilmediyse boş liste), plan dışı id'ler atılır.</summary>
    public static JsonObject Map(DragSolutionDraft draft, JsonObject plan)
    {
        var targets = (plan["targets"] as JsonArray ?? []).OfType<JsonObject>().Select(t => (string?)t["id"])
            .Where(id => id is not null).Select(id => id!).ToList();
        var cards = Ids(plan["cards"]);
        var given = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var target in draft.Targets ?? [])
        {
            var targetId = target?.TargetId?.Trim();
            if (targetId is null || given.ContainsKey(targetId))
                continue;
            given[targetId] = (target!.CardIds ?? []).Select(c => c?.Trim()).Where(c => c is not null && cards.Contains(c))
                .Select(c => c!).Distinct().ToList();
        }

        var solution = new JsonObject();
        foreach (var targetId in targets)
            solution[targetId] = ToArray(given.TryGetValue(targetId, out var list) ? list : []);
        return solution;
    }

    /// <summary>Sol → sağ; örnek (prelinked) sol/sağ ve plan dışı id'ler atılır, her sol ve sağ en çok bir kez (ilk kazanır).</summary>
    public static JsonObject Map(MatchSolutionDraft draft, JsonObject plan)
    {
        var left = Ids(plan["left"]);
        var right = Ids(plan["right"]);
        foreach (var pair in (plan["prelinked"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if ((string?)pair["leftId"] is { } l)
                left.Remove(l);
            if ((string?)pair["rightId"] is { } r)
                right.Remove(r);
        }

        var solution = new JsonObject();
        var usedRight = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in draft.Pairs ?? [])
        {
            var l = pair?.LeftId?.Trim();
            var r = pair?.RightId?.Trim();
            if (l is null || r is null || !left.Contains(l) || !right.Contains(r) || solution.ContainsKey(l) || !usedRight.Add(r))
                continue;
            solution[l] = r;
        }
        return solution;
    }

    private static HashSet<string> Ids(JsonNode? array) =>
        (array as JsonArray ?? []).OfType<JsonObject>().Select(o => (string?)o["id"]).Where(id => id is not null)
            .Select(id => id!).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> Distinct(IEnumerable<string> values, Func<string, string> normalize)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var key = normalize(value);
            if (key.Length > 0 && seen.Add(key))
                yield return value;
        }
    }

    private static JsonArray ToArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
            array.Add(value);
        return array;
    }
}
