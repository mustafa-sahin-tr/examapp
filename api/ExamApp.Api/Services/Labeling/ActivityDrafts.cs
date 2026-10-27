using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #579: tip başına Claude yanıt şemaları (<c>GetResponseAsync&lt;T&gt;</c> bunlardan JSON schema üretir) ve şema →
/// InteractionPlan v1 eşlemesi. Claude koordinatları gönderilen kırpmanın piksel uzayındadır (<c>x1, y1, x2, y2</c>);
/// <see cref="BlockFrame"/> bunları sayfa uzayına çevirir, blok kutusuna kırpar ve blok kutusuna göre göreli yapar (kontrolcülerin
/// beklediği "soru bölgesine göre göreli" uzay). Id'ler sunucuda üretilir; çözüm hiç üretilmez (#580). Şema ya da eşleme değişirse
/// tipin revizyonunu (<see cref="ActivityStructureSettings"/>) artırın.
/// </summary>
internal static class ActivityDrafts
{
    public const int MaxPrintedInstructionLength = 1000;

    // ------------------------------------------------------------------ şemalar

    internal sealed record MatrixDraft(
        [property: Description("The printed instruction sentence, or an empty string.")] string Instruction,
        [property: Description("True only if a row may have more than one marked choice.")] bool MultiPerRow,
        [property: Description("The choices in printed order (2 to 10).")] List<MatrixDraftColumn> Columns,
        [property: Description("Every row the student answers, top to bottom.")] List<MatrixDraftRow> Rows);

    internal sealed record MatrixDraftColumn(
        [property: Description("Printed column heading or symbol.")] string Label);

    internal sealed record MatrixDraftRow(
        [property: Description("The row's printed statement or item, without its number.")] string Label,
        int X1, int Y1, int X2, int Y2,
        [property: Description("Where the student marks each choice of this row.")] List<MatrixDraftCell> Cells);

    internal sealed record MatrixDraftCell(
        [property: Description("0-based index into columns.")] int Column,
        int X1, int Y1, int X2, int Y2);

    internal sealed record SlotDraft(
        [property: Description("The printed instruction sentence, or an empty string.")] string Instruction,
        [property: Description("Every empty place where the student writes an answer, in reading order.")] List<SlotDraftItem> Slots);

    internal sealed record SlotDraftItem(
        [property: Description("Short printed identifier next to the blank; may be empty.")] string Label,
        [property: Description("One of: number, text, select, palette.")] string Kind,
        [property: Description("For number: plain, fraction or mixed; otherwise \"none\".")] string Entry,
        [property: Description("Unit printed right after the blank, or an empty string.")] string Unit,
        [property: Description("For select: the printed alternatives; otherwise empty.")] List<string> Options,
        [property: Description("For palette: the allowed symbols; otherwise empty.")] List<string> Symbols,
        int X1, int Y1, int X2, int Y2);

    internal sealed record DragDraft(
        [property: Description("The printed instruction sentence, or an empty string.")] string Instruction,
        [property: Description("One of: place, order.")] string Mode,
        [property: Description("True only if a card may be used more than once.")] bool Reuse,
        [property: Description("Every place a card goes, in reading order.")] List<DragDraftTarget> Targets,
        [property: Description("Every card of the pool in printed order, including distractors.")] List<DragDraftCard> Cards);

    internal sealed record DragDraftTarget(
        [property: Description("Printed group heading or the text before the blank; may be empty.")] string Label,
        [property: Description("How many cards fit (1 for a blank or a position).")] int Capacity,
        int X1, int Y1, int X2, int Y2);

    internal sealed record DragDraftCard(
        [property: Description("The card's printed text; empty for a picture.")] string Label,
        int X1, int Y1, int X2, int Y2);

    internal sealed record MatchDraft(
        [property: Description("The printed instruction sentence, or an empty string.")] string Instruction,
        [property: Description("Items of the first group in printed order.")] List<MatchDraftLeftItem> Left,
        [property: Description("Items of the second group in printed order (may include distractors).")] List<MatchDraftRightItem> Right,
        [property: Description("Pairs already connected in a printed example (0-based indexes); empty if none.")] List<MatchDraftPair> Prelinked);

    // Sol ve sağ için AYRI tipler: aynı record iki özellikte kullanılınca MEAI ikincisini "$ref" yapar ve Anthropic structured
    // output referanslı şemayı reddeder (400 "References must be defined"). Şekil aynı; LabelingResponseSchemaTests denetler.
    internal sealed record MatchDraftLeftItem(
        [property: Description("The item's printed text without its leading number or letter; empty for a picture.")] string Label,
        int X1, int Y1, int X2, int Y2);

    internal sealed record MatchDraftRightItem(
        [property: Description("The item's printed text without its leading number or letter; empty for a picture.")] string Label,
        int X1, int Y1, int X2, int Y2);

    internal sealed record MatchDraftPair(int Left, int Right);

    // ------------------------------------------------------------------ koordinatlar

    /// <summary>
    /// Kırpma (gönderilen görsel) → sayfa → blok kutusuna göre göreli. Kutu blok kutusuyla kesiştirilir (blok dışı pay atılır;
    /// kutular <c>0 ≤ x, y</c> ve blok içinde kalır); kesişim 1 pikselden küçükse kutu yok (null).
    /// </summary>
    internal sealed record BlockFrame(EncodedRegion Region, Rectangle Block)
    {
        public JsonObject? Box(int x1, int y1, int x2, int y2)
        {
            var rect = Rectangle.Intersect(Region.ToPage(x1, y1, x2, y2), Block);
            if (rect.Width < 1 || rect.Height < 1)
                return null;
            return new JsonObject
            {
                ["x"] = rect.X - Block.X,
                ["y"] = rect.Y - Block.Y,
                ["width"] = rect.Width,
                ["height"] = rect.Height,
            };
        }
    }

    /// <summary>Eşleme sonucu: plan (JSON) ve basılı yönerge.</summary>
    internal sealed record Mapped(JsonObject Plan, string? Instruction);

    // ------------------------------------------------------------------ eşleme

    public static Mapped Map(MatrixDraft draft, BlockFrame frame)
    {
        var columns = new JsonArray();
        var columnCount = Math.Min(draft.Columns?.Count ?? 0, MatrixChoiceGrader.MaxColumns);
        for (var i = 0; i < columnCount; i++)
            columns.Add(Entry($"c{i + 1}", Label(draft.Columns![i]?.Label, MatrixChoiceGrader.MaxLabelLength)));

        var rows = new JsonArray();
        foreach (var row in draft.Rows ?? [])
        {
            if (row is null || rows.Count >= MatrixChoiceGrader.MaxRows)
                continue;
            var cells = new JsonObject();
            foreach (var cell in row.Cells ?? [])
            {
                if (cell is null || cell.Column < 0 || cell.Column >= columnCount)
                    continue;
                var key = $"c{cell.Column + 1}";
                if (cells.ContainsKey(key) || frame.Box(cell.X1, cell.Y1, cell.X2, cell.Y2) is not { } cellBox)
                    continue;
                cells[key] = cellBox;
            }
            // Satır kutusu zorunlu: Claude vermediyse hücrelerin birleşimi; o da yoksa satır alınmaz.
            var box = frame.Box(row.X1, row.Y1, row.X2, row.Y2) ?? Union(cells.Select(c => c.Value as JsonObject));
            if (box is null)
                continue;
            var entry = Entry($"r{rows.Count + 1}", Label(row.Label, MatrixChoiceGrader.MaxLabelLength));
            entry["box"] = box;
            if (cells.Count > 0)
                entry["cells"] = cells;
            rows.Add(entry);
        }

        var plan = new JsonObject
        {
            ["version"] = 1,
            ["type"] = MatrixChoiceGrader.Type,
            ["multiPerRow"] = draft.MultiPerRow,
            ["columns"] = columns,
            ["rows"] = rows,
        };
        return new Mapped(plan, PrintedInstruction(draft.Instruction));
    }

    public static Mapped Map(SlotDraft draft, BlockFrame frame)
    {
        var slots = new JsonArray();
        foreach (var item in draft.Slots ?? [])
        {
            if (item is null || slots.Count >= SlotFillGrader.MaxSlots)
                continue;
            if (frame.Box(item.X1, item.Y1, item.X2, item.Y2) is not { } box)
                continue;
            var id = $"s{slots.Count + 1}";
            var slot = Entry(id, Label(item.Label, SlotFillGrader.MaxLabelLength));
            slot["box"] = box;
            slot["input"] = SlotInput(id, item);
            slots.Add(slot);
        }

        var plan = new JsonObject
        {
            ["version"] = 1,
            ["type"] = SlotFillGrader.Type,
            ["slots"] = slots,
        };
        return new Mapped(plan, PrintedInstruction(draft.Instruction));
    }

    private static JsonObject SlotInput(string slotId, SlotDraftItem item)
    {
        switch (item.Kind?.Trim().ToLowerInvariant())
        {
            case SlotFillGrader.KindNumber:
            {
                var entry = item.Entry?.Trim().ToLowerInvariant() is "fraction" or "mixed" ? item.Entry.Trim().ToLowerInvariant() : "plain";
                var input = new JsonObject { ["kind"] = SlotFillGrader.KindNumber, ["entry"] = entry };
                if (Label(item.Unit, SlotFillGrader.MaxUnitHintLength) is { } unit)
                    input["unit"] = unit;
                return input;
            }
            case SlotFillGrader.KindSelect:
            {
                var options = new JsonArray();
                foreach (var label in Distinct(item.Options, SlotFillGrader.MaxLabelLength).Take(SlotFillGrader.MaxOptions))
                    options.Add(new JsonObject { ["id"] = $"{slotId}o{options.Count + 1}", ["label"] = label });
                return new JsonObject { ["kind"] = SlotFillGrader.KindSelect, ["options"] = options };
            }
            case SlotFillGrader.KindPalette:
            {
                var symbols = new JsonArray();
                foreach (var symbol in Distinct(item.Symbols, int.MaxValue)
                             .Where(s => s.Length <= SlotFillGrader.MaxSymbolLength).Take(SlotFillGrader.MaxSymbols))
                    symbols.Add(symbol);
                return new JsonObject { ["kind"] = SlotFillGrader.KindPalette, ["symbols"] = symbols };
            }
            default:
                return new JsonObject { ["kind"] = SlotFillGrader.KindText };
        }
    }

    public static Mapped Map(DragDraft draft, BlockFrame frame)
    {
        var order = string.Equals(draft.Mode?.Trim(), DragPlaceGrader.ModeOrder, StringComparison.OrdinalIgnoreCase);
        var targets = new JsonArray();
        foreach (var target in draft.Targets ?? [])
        {
            if (target is null || targets.Count >= DragPlaceGrader.MaxTargets)
                continue;
            var entry = Entry($"t{targets.Count + 1}", Label(target.Label, DragPlaceGrader.MaxLabelLength));
            if (frame.Box(target.X1, target.Y1, target.X2, target.Y2) is { } box)
                entry["box"] = box;
            entry["capacity"] = order ? 1 : Math.Clamp(target.Capacity, DragPlaceGrader.MinCapacity, DragPlaceGrader.MaxCapacity);
            targets.Add(entry);
        }

        var cards = new JsonArray();
        foreach (var card in draft.Cards ?? [])
        {
            if (card is null || cards.Count >= DragPlaceGrader.MaxCards)
                continue;
            var label = Label(card.Label, DragPlaceGrader.MaxLabelLength);
            var box = frame.Box(card.X1, card.Y1, card.X2, card.Y2);
            // Kart yüzü: etiket ya da görsel kırpması (kutu) zorunlu.
            if (label is null && box is null)
                continue;
            var entry = Entry($"k{cards.Count + 1}", label);
            if (box is not null)
                entry["box"] = box;
            cards.Add(entry);
        }

        var plan = new JsonObject
        {
            ["version"] = 1,
            ["type"] = DragPlaceGrader.Type,
            ["mode"] = order ? DragPlaceGrader.ModeOrder : DragPlaceGrader.ModePlace,
            ["reuse"] = !order && draft.Reuse,
            ["targets"] = targets,
            ["cards"] = cards,
        };
        return new Mapped(plan, PrintedInstruction(draft.Instruction));
    }

    public static Mapped Map(MatchDraft draft, BlockFrame frame)
    {
        var (left, leftIds) = MatchItems(draft.Left?.Select(i => i is null ? null : new MatchItem(i.Label, i.X1, i.Y1, i.X2, i.Y2)).ToList(), "l", frame);
        var (right, rightIds) = MatchItems(draft.Right?.Select(i => i is null ? null : new MatchItem(i.Label, i.X1, i.Y1, i.X2, i.Y2)).ToList(), "r", frame);

        var prelinked = new JsonArray();
        var usedLeft = new HashSet<string>(StringComparer.Ordinal);
        var usedRight = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in draft.Prelinked ?? [])
        {
            if (pair is null || !leftIds.TryGetValue(pair.Left, out var leftId) || !rightIds.TryGetValue(pair.Right, out var rightId))
                continue;
            if (!usedLeft.Add(leftId) || !usedRight.Add(rightId))
                continue;
            prelinked.Add(new JsonObject { ["leftId"] = leftId, ["rightId"] = rightId });
        }

        var plan = new JsonObject
        {
            ["version"] = 1,
            ["type"] = MatchGrader.Type,
            ["left"] = left,
            ["right"] = right,
            ["prelinked"] = prelinked,
        };
        return new Mapped(plan, PrintedInstruction(draft.Instruction));
    }

    /// <summary>Öğeler (etiket ya da kutu zorunlu) ve Claude'un 0 tabanlı sırası → üretilen id (örnek çiftler için).</summary>
    private sealed record MatchItem(string? Label, int X1, int Y1, int X2, int Y2);

    private static (JsonArray Items, Dictionary<int, string> IdsByIndex) MatchItems(
        List<MatchItem?>? items, string prefix, BlockFrame frame)
    {
        var result = new JsonArray();
        var ids = new Dictionary<int, string>();
        for (var i = 0; i < (items?.Count ?? 0); i++)
        {
            var item = items![i];
            if (item is null || result.Count >= MatchGrader.MaxItems)
                continue;
            var label = Label(item.Label, MatchGrader.MaxLabelLength);
            var box = frame.Box(item.X1, item.Y1, item.X2, item.Y2);
            if (label is null && box is null)
                continue;
            var id = $"{prefix}{result.Count + 1}";
            var entry = Entry(id, label);
            if (box is not null)
                entry["box"] = box;
            result.Add(entry);
            ids[i] = id;
        }
        return (result, ids);
    }

    // ------------------------------------------------------------------ yardımcılar

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private static JsonObject Entry(string id, string? label)
    {
        var entry = new JsonObject { ["id"] = id };
        if (label is not null)
            entry["label"] = label;
        return entry;
    }

    /// <summary>Boşluklar teke indirilir, kırpılır; boşsa null, uzunsa kesilir.</summary>
    internal static string? Label(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var cleaned = Whitespace.Replace(value, " ").Trim();
        if (cleaned.Length == 0 || cleaned.Equals("none", StringComparison.OrdinalIgnoreCase))
            return null;
        return cleaned.Length <= max ? cleaned : cleaned[..max].TrimEnd();
    }

    internal static string? PrintedInstruction(string? value) => Label(value, MaxPrintedInstructionLength);

    private static IEnumerable<string> Distinct(List<string>? values, int max)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values ?? [])
        {
            if (Label(value, max) is { } label && seen.Add(label))
                yield return label;
        }
    }

    private static JsonObject? Union(IEnumerable<JsonObject?> boxes)
    {
        Rectangle? union = null;
        foreach (var b in boxes)
        {
            if (b is null)
                continue;
            var r = new Rectangle((int)b["x"]!, (int)b["y"]!, (int)b["width"]!, (int)b["height"]!);
            union = union is { } u ? Rectangle.Union(u, r) : r;
        }
        return union is { } x
            ? new JsonObject { ["x"] = x.X, ["y"] = x.Y, ["width"] = x.Width, ["height"] = x.Height }
            : null;
    }
}

/// <summary>
/// Issue #579: taslak planın neden kaydedilemeyeceğine dair öğretmene gösterilecek ipuçları (yerelleştirme anahtarları).
/// Asıl karar tipin <c>ValidateDefinition</c>'ıdır; bu sınıf yalnız sık görülen nedenleri adlandırır, hiçbiri tutmazsa
/// genel <see cref="Generic"/> anahtarı döner.
/// </summary>
internal static class ActivityPlanDiagnostics
{
    public const string Generic = "labeling.activity.invalid.definition";

    public static IReadOnlyList<string> Explain(string interactionType, string planJson)
    {
        var errors = new List<string>();
        try
        {
            if (JsonNode.Parse(planJson) is not JsonObject plan)
                return [Generic];

            switch (interactionType)
            {
                case MatrixChoiceGrader.Type:
                    if (Count(plan, "columns") < MatrixChoiceGrader.MinColumns)
                        errors.Add("labeling.activity.invalid.tooFewColumns");
                    if (Count(plan, "rows") < MatrixChoiceGrader.MinRows)
                        errors.Add("labeling.activity.invalid.noRows");
                    break;
                case SlotFillGrader.Type:
                    var slots = plan["slots"] as JsonArray ?? [];
                    if (slots.Count < SlotFillGrader.MinSlots)
                        errors.Add("labeling.activity.invalid.noSlots");
                    var inputs = slots.Select(s => s?["input"] as JsonObject).Where(i => i is not null).ToList();
                    if (inputs.Any(i => (string?)i!["kind"] == SlotFillGrader.KindSelect && Count(i!, "options") < SlotFillGrader.MinOptions))
                        errors.Add("labeling.activity.invalid.selectOptions");
                    if (inputs.Any(i => (string?)i!["kind"] == SlotFillGrader.KindPalette && Count(i!, "symbols") < SlotFillGrader.MinSymbols))
                        errors.Add("labeling.activity.invalid.paletteSymbols");
                    break;
                case DragPlaceGrader.Type:
                    if (Count(plan, "targets") < DragPlaceGrader.MinTargets)
                        errors.Add("labeling.activity.invalid.noTargets");
                    if (Count(plan, "cards") < DragPlaceGrader.MinCards)
                        errors.Add("labeling.activity.invalid.noCards");
                    break;
                case MatchGrader.Type:
                    if (Count(plan, "left") < MatchGrader.MinItems || Count(plan, "right") < MatchGrader.MinItems)
                        errors.Add("labeling.activity.invalid.matchItems");
                    else if (Count(plan, "prelinked") >= Count(plan, "left"))
                        errors.Add("labeling.activity.invalid.matchAllPrelinked");
                    break;
            }
        }
        catch (JsonException)
        {
            return [Generic];
        }

        return errors.Count > 0 ? errors : [Generic];
    }

    private static int Count(JsonObject obj, string name) => (obj[name] as JsonArray)?.Count ?? 0;
}
