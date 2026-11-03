using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
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
    private const int FormatVersion = 1;

    public static QuestionContentHashes Compute(byte[]? image, string? text, string? subText, string? interactionType, string? plan, string? solution,
        string? practiceAnswer, IEnumerable<QuestionContentAnswer> answers)
    {
        var answerRows = answers
            .Select(a => new { Text = Normalize(a.Text), Tag = Normalize(a.Tag), a.Order, a.IsCorrect })
            .OrderBy(a => a.Order ?? int.MaxValue)
            .ThenBy(a => a.Tag, StringComparer.Ordinal)
            .ThenBy(a => a.Text, StringComparer.Ordinal)
            .ThenBy(a => a.IsCorrect)
            .ToList();
        var canonicalPlan = CanonicalJson(plan);
        var canonicalSolution = CanonicalJson(solution);
        var type = Normalize(interactionType);
        var normalizedText = Normalize(text);
        var normalizedSubText = Normalize(subText);
        var normalizedPractice = Normalize(practiceAnswer);

        var canonical = JsonSerializer.Serialize(new
        {
            v = FormatVersion,
            text = normalizedText,
            subText = normalizedSubText,
            type,
            plan = canonicalPlan,
            solution = canonicalSolution,
            practice = normalizedPractice,
            answers = answerRows,
        });
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        if (image is { Length: > 0 })
            return new QuestionContentHashes(Convert.ToHexStringLower(SHA256.HashData(image)), fingerprint);

        // Görseli ve metni olmayan satırlar birbirine eşit görünür: kimlik verilmez, böylece yanlış eşleşmez.
        var hasContent = normalizedText.Length > 0 || normalizedSubText.Length > 0 || canonicalPlan.Length > 0
            || answerRows.Any(a => a.Text.Length > 0);
        return new QuestionContentHashes(hasContent ? fingerprint : null, fingerprint);
    }

    /// <summary>Kayıtlı soru: dışa aktarımın pakete yazdığı plan ve çözümle aynı değerlerden hesaplanır.</summary>
    public static QuestionContentHashes ForQuestion(Question question, byte[]? image)
    {
        var (plan, embeddedSolution) = InteractionPlanSolution.Split(question.InteractionPlan);
        return Compute(image, question.Text, question.SubText, question.InteractionType, plan, question.InteractionSolution ?? embeddedSolution,
            question.PracticeCorrectAnswer,
            question.Answers.Select(a => new QuestionContentAnswer(a.Text, a.Tag, a.Order, question.CorrectAnswerId == a.Id)));
    }

    /// <summary>Paketteki soru: içe aktarım öncesi, hedefle karşılaştırmak için.</summary>
    public static QuestionContentHashes ForManifest(JsonElement question, byte[]? image)
    {
        var (plan, solution) = QuestionTransferJobRunner.ReadInteractionPlanAndSolution(question);
        var answers = new List<QuestionContentAnswer>();
        if (question.TryGetProperty("answers", out var answersEl) && answersEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var answer in answersEl.EnumerateArray())
            {
                answers.Add(new QuestionContentAnswer(
                    ReadString(answer, "text"),
                    ReadString(answer, "tag"),
                    answer.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Number ? order.GetInt32() : null,
                    answer.TryGetProperty("isCorrect", out var correct) && correct.ValueKind == JsonValueKind.True));
            }
        }

        return Compute(image, ReadString(question, "text"), ReadString(question, "subText"), ReadString(question, "interactionType"), plan, solution,
            ReadString(question, "practiceCorrectAnswer"), answers);
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
