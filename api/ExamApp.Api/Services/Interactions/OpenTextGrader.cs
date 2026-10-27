using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #583: <c>openText</c> kontrolcüsü (bileşen H, açık uçlu cevap) — AI'sız ilk sürüm. Otomatik kontrol YOK: öğrenci metni
/// kaydedilir, öğretmen değerlendirir (<c>OpenTextReview</c>: yeterli / kısmen / eksik + geri bildirim, puansız).
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"openText",minWords?,maxWords?,lines?}</c>; <c>minWords</c>/<c>maxWords</c> 1..2000 ve
/// min ≤ max, <c>lines</c> (metin kutusunun önerilen satır sayısı) 1..30; hepsi isteğe bağlı (null = sınırsız / varsayılan).
/// Yönerge görselde / soru metninde, plan taşımaz.</item>
/// <item>Çözüm (<c>Question.InteractionSolution</c>): <c>{sampleAnswer?,rubric?}</c>, her biri ≤ 4000 karakter düz metin; boş
/// çözüm geçerli (değerlendirme elle). Öğrenciye yalnız <c>sampleAnswer</c> gider (<see cref="StudentVisibleSolution"/>),
/// <c>rubric</c> yalnız öğretmene (sahip / değerlendirme kuyruğu).</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"openText",text}</c>; <c>text</c> ≤ <see cref="MaxTextLength"/> karakter, kontrol
/// karakteri yok (\n, \r, \t serbest). Boş/boşluk = cevapsız (Normalize → null). Sözcük sınırı kayıtta zorlanmaz; yalnız
/// <c>maxWords</c>'ün <see cref="MaxWordsOverflowFactor"/> katını aşan metin reddedilir (400).</item>
/// </list>
/// <see cref="Grade"/> her zaman 0/0 döner (doğru/yanlış sayımlarına, etkinliğin ilk kontrol özetine girmez); cevap varsa geri
/// bildirim <c>{"rows":[{"id":"text","status":"pending"}]}</c>. Değerlendirilmiş satırda çıktı yolları durumu
/// <see cref="FeedbackFor"/> ile değerlendirmeye çevirir.
/// </summary>
public sealed class OpenTextGrader : IInteractionGrader
{
    public const string Type = "openText";

    /// <summary>Geri bildirimdeki tek satırın id'si.</summary>
    public const string RowId = "text";

    public const int MinWordLimit = 1;
    public const int MaxWordLimit = 2000;
    public const int MinLines = 1;
    public const int MaxLines = 30;
    public const int MaxSolutionTextLength = 4000;

    /// <summary>Öğrencinin yazabileceği en uzun metin (karakter; 16 KB payload sınırının içinde).</summary>
    public const int MaxTextLength = 8000;

    /// <summary><c>maxWords</c> doluysa metin en çok bu kat sözcük içerebilir (yazarken sayaç uyarır; fazlası reddedilir).</summary>
    public const int MaxWordsOverflowFactor = 2;

    public const string StatusPending = "pending";

    private static readonly HashSet<string> PlanKeys = new(StringComparer.Ordinal) { "version", "type", "minWords", "maxWords", "lines" };
    private static readonly HashSet<string> SolutionKeys = new(StringComparer.Ordinal) { "sampleAnswer", "rubric" };
    private static readonly HashSet<string> PayloadKeys = new(StringComparer.Ordinal) { "version", "type", "text" };

    private static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 16, AllowDuplicateProperties = false };
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

    // Harfler kaçışsız yazılır (Türkçe metin \uXXXX ile 6 kat büyümesin); HTML'e duyarlı karakterler (<, >, &, ', ") yine kaçışlı.
    private static readonly JavaScriptEncoder TextEncoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = TextEncoder };
    private static readonly JsonSerializerOptions SerializerOptions = new() { Encoder = TextEncoder };

    public string InteractionType => Type;

    /// <summary>Plan sınırları (okuma: bozuk alan sınırsız sayılır).</summary>
    public readonly record struct PlanLimits(int? MinWords, int? MaxWords, int? Lines);

    // ================================================================== IInteractionGrader

    public bool Validate(string? plan, string? payload)
    {
        if (payload == null)
            return true;
        if (payload.Length > InteractionPayload.MaxLength)
            return false;
        if (!TryParsePayload(payload, out var text))
            return false;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        if (!TryReadPlan(plan, ReadOptions, strict: false, out var limits))
            return false;
        return limits.MaxWords is not { } max || CountWords(text) <= max * MaxWordsOverflowFactor;
    }

    /// <summary>Boş/boşluk metin = cevap yok → null; aksi halde kanonik payload (satır sonları \n, baş/son boşluk kırpılmış).</summary>
    public string? Normalize(string? payload)
    {
        if (payload == null || !TryParsePayload(payload, out var text))
            return payload;
        var normalized = NormalizeText(text ?? string.Empty);
        return normalized.Length == 0 ? null : WritePayload(normalized);
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        var answered = input.Payload != null && TryParsePayload(input.Payload, out var text) && !string.IsNullOrWhiteSpace(text);
        return answered ? new InteractionGradeResult(false, 0, 0, FeedbackFor(StatusPending)) : InteractionGradeResult.Ungradable;
    }

    /// <summary>Kayıt kuralı: plan zorunlu ve şemaya uygun; çözüm boş ya da yalnız <c>sampleAnswer</c>/<c>rubric</c> (düz metin, ≤ 4000).</summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, StrictOptions, strict: true, out _))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(solution, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var property in root.EnumerateObject())
            {
                if (!SolutionKeys.Contains(property.Name))
                    return false;
                if (property.Value.ValueKind == JsonValueKind.Null)
                    continue;
                if (property.Value.ValueKind != JsonValueKind.String)
                    return false;
                var value = property.Value.GetString()!;
                if (value.Length > MaxSolutionTextLength || PlainTextAttribute.ContainsControlCharacters(value))
                    return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ================================================================== yardımcılar (servisler de kullanır)

    /// <summary>Planın sınırları; okunamayan plan → hepsi null.</summary>
    public static PlanLimits ReadLimits(string? plan)
        => TryReadPlan(plan, ReadOptions, strict: false, out var limits) ? limits : default;

    /// <summary>Saklanan (normalize edilmiş) payload'un metni; okunamazsa / boşsa null.</summary>
    public static string? ReadText(string? payload)
        => payload != null && TryParsePayload(payload, out var text) && !string.IsNullOrWhiteSpace(text) ? NormalizeText(text!) : null;

    /// <summary>
    /// Değerlendirilen cevabın özeti: normalize metnin UTF-8 SHA-256'sı (küçük harf hex). Değerlendirmenin güncelliği buna göre
    /// verilir — "Cevapları kontrol et" metin değişmeden de <c>AnswerRevision</c>'ı ilerletir, revizyon bu yüzden kullanılmaz.
    /// </summary>
    public static string? AnswerHash(string? payload)
    {
        var text = ReadText(payload);
        return text == null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>Sözcük sayısı: boşluk olmayan karakterlerin kesintisiz dizileri (istemci sayacı aynı kuralı uygular).</summary>
    public static int CountWords(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }

    /// <summary>Tek satırlık geri bildirim: <c>{"rows":[{"id":"text","status":...}]}</c>.</summary>
    public static string FeedbackFor(string status)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("rows");
            writer.WriteStartObject();
            writer.WriteString("id", RowId);
            writer.WriteString("status", status);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static bool IsOpenText(string? interactionType)
        => interactionType != null && interactionType.Trim().Equals(Type, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Öğrenciye giden çözüm: <c>openText</c>'te yalnız <c>sampleAnswer</c> (<c>{"sampleAnswer":"..."}</c>; yoksa null) — rubrik
    /// öğretmene özeldir. Diğer tipler aynen. İstisna atmaz; okunamayan openText çözümü null.
    /// </summary>
    public static string? StudentVisibleSolution(string? interactionType, string? solution)
    {
        if (!IsOpenText(interactionType) || string.IsNullOrWhiteSpace(solution))
            return solution;
        var (sample, _) = ReadSolution(solution);
        if (sample == null)
            return null;
        return JsonSerializer.Serialize(new Dictionary<string, string> { ["sampleAnswer"] = sample }, SerializerOptions);
    }

    /// <summary>Çözümün örnek cevabı ve rubriği (boş/okunamayan → null).</summary>
    public static (string? SampleAnswer, string? Rubric) ReadSolution(string? solution)
    {
        if (string.IsNullOrWhiteSpace(solution))
            return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return (null, null);
            return (ReadString(doc.RootElement, "sampleAnswer"), ReadString(doc.RootElement, "rubric"));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return (null, null);
        }
    }

    // ================================================================== iç

    private static string? ReadString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static string NormalizeText(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static string WritePayload(string text)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteString("type", Type);
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static bool TryParsePayload(string payload, out string? text)
    {
        text = null;
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var property in root.EnumerateObject())
            {
                if (!PayloadKeys.Contains(property.Name))
                    return false;
            }
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (root.TryGetProperty("text", out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                    text = value.GetString();
                else if (value.ValueKind != JsonValueKind.Null)
                    return false;
            }
            if (text != null && (text.Length > MaxTextLength || PlainTextAttribute.ContainsControlCharacters(text)))
            {
                text = null;
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            text = null;
            return false;
        }
    }

    /// <summary>
    /// Plan okuma. <paramref name="strict"/> false (okuma/kontrol): tip/sürüm aranmaz, bozuk sınır null sayılır. true (kayıt):
    /// izinli alanlar, tip, sürüm, sınır aralıkları, min ≤ max.
    /// </summary>
    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out PlanLimits limits)
    {
        limits = default;
        if (string.IsNullOrWhiteSpace(plan))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(plan, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (strict)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (!PlanKeys.Contains(property.Name))
                        return false;
                }
                if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                    || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                    return false;
                if (root.TryGetProperty("version", out var version)
                    && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                    return false;
            }

            if (!TryReadOptionalInt(root, "minWords", MinWordLimit, MaxWordLimit, strict, out var min)
                || !TryReadOptionalInt(root, "maxWords", MinWordLimit, MaxWordLimit, strict, out var max)
                || !TryReadOptionalInt(root, "lines", MinLines, MaxLines, strict, out var lines))
                return false;
            if (min is { } a && max is { } b && a > b)
            {
                if (strict)
                    return false;
                min = null;
            }

            limits = new PlanLimits(min, max, lines);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Yok / null → null. Aralık dışı ya da tam sayı olmayan: strict'te false, okumada null.</summary>
    private static bool TryReadOptionalInt(JsonElement root, string name, int min, int max, bool strict, out int? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return true;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed) && parsed >= min && parsed <= max)
        {
            value = parsed;
            return true;
        }
        return !strict;
    }
}
