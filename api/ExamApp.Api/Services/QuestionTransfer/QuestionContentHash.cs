using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interactions;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.QuestionTransfer;

/// <summary>Sorunun ortamlar arasında eşleşmesini sağlayan iki değer.</summary>
/// <param name="ContentHash">Kimlik: görselli soruda görselin SHA-256'sı, görselsiz soruda <paramref name="Fingerprint"/>. İçeriği yoksa null: eşleşmez.</param>
/// <param name="Fingerprint">Metin, şıklar, doğru cevap, plan ve çözümün SHA-256'sı. Aynı görsel farklı cevapla kullanılmışsa farkı bu gösterir.</param>
public sealed record QuestionContentHashes(string? ContentHash, string Fingerprint);

public sealed record QuestionContentAnswer(string? Text, string? Tag, int? Order, bool IsCorrect);

public enum QuestionMatchKind
{
    New,
    Exact,
    Conflict,
}

public sealed record QuestionMatch(QuestionMatchKind Kind, int? TargetQuestionId);

public static class QuestionContentHash
{
    private const int FormatVersion = 3;

    /// <summary>Paketteki tek görselin üst sınırı. Yüklenen zip belleğe açıldığı için sınırsız okunmaz.</summary>
    internal const int MaxPackageImageBytes = 50 * 1024 * 1024;

    /// <summary>Paketteki manifestin üst sınırı.</summary>
    internal const int MaxPackageManifestBytes = 256 * 1024 * 1024;

    /// <param name="identifiedByText">
    /// Görsel yoksa kimlik metinden gelir. Metni olmayan paragrafa ya da şık görseline bağlı soruda metin soruyu tanımlamaz:
    /// false verilirse kimlik oluşmaz, farklı sorular tek soruda birleşmez.
    /// </param>
    /// <param name="passageText">Görselsiz soruda parmak izine girer: "Buna göre hangisi doğrudur?" paragrafıyla birlikte tanımlıdır.</param>
    public static QuestionContentHashes Compute(byte[]? image, string? text, string? subText, string? interactionType, string? plan, string? solution,
        string? practiceAnswer, IEnumerable<QuestionContentAnswer> answers, bool identifiedByText = true, string? passageText = null)
    {
        var answerRows = answers
            .Select(a => new { Text = Normalize(a.Text), Tag = Normalize(a.Tag), a.Order, a.IsCorrect })
            .OrderBy(a => a.Order ?? int.MaxValue)
            .ThenBy(a => a.Tag, StringComparer.Ordinal)
            .ThenBy(a => a.Text, StringComparer.Ordinal)
            .ThenBy(a => a.IsCorrect)
            .ToList();
        var type = Normalize(interactionType);
        var (canonicalPlan, canonicalSolution) = CanonicalInteraction(type, plan, solution);
        // Kayıt metni temizleyerek yazar: paket ham, satır temiz olsa da aynı parmak izi çıksın.
        var normalizedText = NormalizeRichText(text);
        var normalizedSubText = NormalizeRichText(subText);
        var normalizedPractice = NormalizeRichText(practiceAnswer);

        var canonical = JsonSerializer.Serialize(new
        {
            v = FormatVersion,
            text = normalizedText,
            subText = normalizedSubText,
            type,
            plan = canonicalPlan,
            solution = canonicalSolution,
            practice = normalizedPractice,
            passage = image is { Length: > 0 } ? string.Empty : Normalize(passageText),
            answers = answerRows,
        });
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        if (image is { Length: > 0 })
            return new QuestionContentHashes(Convert.ToHexStringLower(SHA256.HashData(image)), fingerprint);

        // Görseli ve metni olmayan satırlar birbirine eşit görünür: kimlik verilmez, böylece yanlış eşleşmez.
        var hasContent = identifiedByText && (normalizedText.Length > 0 || normalizedSubText.Length > 0 || canonicalPlan.Length > 0
            || answerRows.Any(a => a.Text.Length > 0));
        return new QuestionContentHashes(hasContent ? fingerprint : null, fingerprint);
    }

    /// <summary>Kayıtlı soru: dışa aktarımın pakete yazdığı plan ve çözümle aynı değerlerden hesaplanır.</summary>
    public static QuestionContentHashes ForQuestion(Question question, byte[]? image)
    {
        var (plan, embeddedSolution) = InteractionPlanSolution.Split(question.InteractionPlan);
        var passageText = PassageText(question.Passage?.Title, question.Passage?.Text);
        return Compute(image, question.Text, question.SubText, question.InteractionType, plan, question.InteractionSolution ?? embeddedSolution,
            question.PracticeCorrectAnswer,
            question.Answers.Select(a => new QuestionContentAnswer(a.Text, a.Tag, a.Order, question.CorrectAnswerId == a.Id)),
            identifiedByText: (question.PassageId is null || passageText.Length > 0) && !question.Answers.Any(a => !string.IsNullOrWhiteSpace(a.ImageUrl)),
            passageText: passageText);
    }

    /// <summary>Paketteki soru: içe aktarım öncesi, hedefle karşılaştırmak için.</summary>
    public static QuestionContentHashes ForManifest(JsonElement question, byte[]? image)
    {
        var (plan, solution) = QuestionTransferJobRunner.ReadInteractionPlanAndSolution(question);
        var answers = new List<QuestionContentAnswer>();
        var hasAnswerImage = false;
        if (question.TryGetProperty("answers", out var answersEl) && answersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var answer in answersEl.EnumerateArray())
            {
                hasAnswerImage |= !string.IsNullOrWhiteSpace(ReadString(answer, "imageUrl"));
                answers.Add(new QuestionContentAnswer(
                    ReadString(answer, "text"),
                    ReadString(answer, "tag"),
                    answer.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Number ? order.GetInt32() : null,
                    answer.TryGetProperty("isCorrect", out var correct) && correct.ValueKind == JsonValueKind.True));
            }
        }

        var hasPassage = question.TryGetProperty("passage", out var passage) && passage.ValueKind == JsonValueKind.Object;
        var passageText = hasPassage ? PassageText(ReadString(passage, "title"), ReadString(passage, "text")) : string.Empty;
        return Compute(image, ReadString(question, "text"), ReadString(question, "subText"), ReadString(question, "interactionType"), plan, solution,
            ReadString(question, "practiceCorrectAnswer"), answers,
            identifiedByText: (!hasPassage || passageText.Length > 0) && !hasAnswerImage, passageText: passageText);
    }

    /// <summary>
    /// Paketin kaynağı: daha önce alınan soru bununla tanınır. Her dışa aktarımın adı farklıdır, bu yüzden önce ortamın kalıcı kimliğine bakılır;
    /// eski pakette ad kullanılır.
    /// </summary>
    public static string? ReadPackageSourceKey(JsonElement manifest)
    {
        var origin = ReadString(manifest, "originKey");
        return string.IsNullOrWhiteSpace(origin) ? ReadString(manifest, "sourceKey") : origin;
    }

    /// <summary>Manifest görsel gösteriyor ama zipte yok. Böyle bir soru metninden kimlik alırsa görselsiz başka sorularla birleşir.</summary>
    public static bool IsImageMissing(JsonElement question, byte[]? image) =>
        image is not { Length: > 0 } && !string.IsNullOrWhiteSpace(ReadString(question, "imageUrl"));

    /// <summary>Zip girdisini sınırla okur. Bildirilen boyuta güvenilmez, okunan bayt da sayılır.</summary>
    internal static async Task<byte[]> ReadPackageEntryAsync(ZipArchiveEntry entry, int maxBytes, CancellationToken ct = default)
    {
        if (entry.Length > maxBytes)
            throw new InvalidOperationException($"{entry.FullName} is larger than the {maxBytes / (1024 * 1024)} MB limit.");

        await using var stream = entry.Open();
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new InvalidOperationException($"{entry.FullName} is larger than the {maxBytes / (1024 * 1024)} MB limit.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Hash aynı ve parmak izi aynıysa aynı sorudur. Hash aynı ama parmak izi farklıysa aynı görsel başka cevapla kullanılmıştır:
    /// çakışma sayılır, kopya oluşturulmaz ve hedefteki soru değiştirilmez.
    /// </summary>
    public static async Task<QuestionMatch> FindMatchAsync(AppDbContext db, QuestionContentHashes hashes, CancellationToken ct = default)
    {
        if (hashes.ContentHash is null)
            return new QuestionMatch(QuestionMatchKind.New, null);

        var rows = await db.Questions.AsNoTracking()
            .Where(q => q.ContentHash == hashes.ContentHash)
            .OrderBy(q => q.Id)
            .Select(q => new { q.Id, q.ContentFingerprint })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return new QuestionMatch(QuestionMatchKind.New, null);

        var exact = rows.FirstOrDefault(r => r.ContentFingerprint == hashes.Fingerprint);
        return exact is not null
            ? new QuestionMatch(QuestionMatchKind.Exact, exact.Id)
            : new QuestionMatch(QuestionMatchKind.Conflict, rows[0].Id);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizeRichText(string? value) => Normalize(RichTextSanitizer.Sanitize(value));

    private static string PassageText(string? title, string? text) => $"{NormalizeRichText(title)}\n{NormalizeRichText(text)}".Trim();

    /// <summary>
    /// dragPlace ve match kayıtta karıştırılır ve opak id alır: aynı soru her ortamda başka sıra ve id taşır. Parmak izi bu yüzden
    /// id yerine öğenin içeriğine bakar, öğeleri de içeriğe göre sıralar. Beklenmeyen biçimde düz JSON karşılaştırılır.
    /// </summary>
    private static (string Plan, string Solution) CanonicalInteraction(string type, string? plan, string? solution)
    {
        var listName = type switch
        {
            DragPlaceGrader.Type => "cards",
            MatchGrader.Type => "right",
            _ => null,
        };
        if (listName is null || string.IsNullOrWhiteSpace(plan))
            return (CanonicalJson(plan), CanonicalJson(solution));

        try
        {
            if (JsonNode.Parse(plan) is not JsonObject root || root[listName] is not JsonArray items)
                return (CanonicalJson(plan), CanonicalJson(solution));

            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
            var sorted = new List<(string Token, JsonNode Node)>();
            foreach (var item in items)
            {
                var copy = (JsonObject)item!.DeepClone();
                var id = copy["id"]!.GetValue<string>();
                copy.Remove("id");
                var token = Sorted(copy)!.ToJsonString();
                tokens[id] = token;
                sorted.Add((token, copy));
            }
            root[listName] = new JsonArray(sorted.OrderBy(i => i.Token, StringComparer.Ordinal).Select(i => i.Node).ToArray());

            if (root["prelinked"] is JsonArray prelinked)
            {
                foreach (var pair in prelinked)
                    pair!["rightId"] = tokens[pair["rightId"]!.GetValue<string>()];
            }

            return (Sorted(root)!.ToJsonString(), CanonicalSolution(type, solution, tokens));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or NullReferenceException or InvalidCastException)
        {
            return (CanonicalJson(plan), CanonicalJson(solution));
        }
    }

    private static string CanonicalSolution(string type, string? solution, Dictionary<string, string> tokens)
    {
        if (string.IsNullOrWhiteSpace(solution))
            return string.Empty;
        if (JsonNode.Parse(solution) is not JsonObject root)
            return CanonicalJson(solution);

        foreach (var key in root.Select(p => p.Key).ToList())
        {
            if (type == MatchGrader.Type)
            {
                root[key] = tokens[root[key]!.GetValue<string>()];
                continue;
            }

            // dragPlace: { hedef: [kart, ...] }. Sıra modunda dizilim çözümün parçasıdır, olduğu gibi kalır.
            var list = (JsonArray)root[key]!;
            for (var i = 0; i < list.Count; i++)
                list[i] = tokens[list[i]!.GetValue<string>()];
        }
        return Sorted(root)!.ToJsonString();
    }

    /// <summary>Anahtar sırası ve boşluk farkı içerik farkı sayılmaz. Ayrıştırılamayan eski değer olduğu gibi karşılaştırılır.</summary>
    private static string CanonicalJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;
        try
        {
            return Sorted(JsonNode.Parse(json))?.ToJsonString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return json.Trim();
        }
    }

    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Sorted).ToArray()),
        _ => node?.DeepClone(),
    };
}
