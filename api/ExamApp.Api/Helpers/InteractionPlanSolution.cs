using System.IO;
using System.Text;
using System.Text.Json;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #570: etkileşimli sorunun (ör. <c>dragDropLabeling</c>) çözümü plandan ayrı saklanır
/// (<c>Question.InteractionSolution</c>, plan içindeki eski <c>solution</c> alanının DEĞERİ, ör. <c>{"placements":[...]}</c>);
/// plan (<c>Question.InteractionPlan</c>) çözümsüz saklanır ve öğrenciye çözümsüz gider.
/// <para>
/// Okuma/çıktı yolu (<see cref="Split"/>, <see cref="ForOutput"/>) hiçbir girdide istisna atmaz: her öğrenci test/alıştırma
/// sorusunda çalıştığı için tek bir bozuk eski plan tüm testi 500'e düşürmemeli. Ayrıştırma <see cref="JsonDocument"/> ile
/// yapılır (yinelenen anahtara toleranslı; <c>JsonNode</c> yinelenen anahtarda tembel <see cref="ArgumentException"/> atar).
/// Yazma yolu (<see cref="TryResolveForWrite"/>) katıdır: geçersiz JSON, nesne olmayan değer, yinelenen anahtar ya da
/// sınır aşımı reddedilir (çağıran InvalidInput / 400 döner, ham istisna mesajı yüzeye çıkmaz).
/// </para>
/// </summary>
public static class InteractionPlanSolution
{
    public const string SolutionKey = "solution";

    /// <summary>Çözümün üst sınırı (karakter); DTO'larda <c>[MaxLength]</c>, yazmada <see cref="IsValidSolution"/>.</summary>
    public const int MaxSolutionLength = 64 * 1024;

    /// <summary>Planın üst sınırı (karakter); DTO'larda <c>[MaxLength]</c>, yazmada <see cref="IsValidPlan"/>.</summary>
    public const int MaxPlanLength = 256 * 1024;

    // Okuma: yinelenen anahtar serbest (eski veri), derinlik tarayıcının ayrıştırabileceği planları da kapsasın diye geniş.
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

    // Yazma: yinelenen anahtar yasak (istemci JS son değeri, sunucu başka değeri görebilirdi — belirsiz veri saklanmaz).
    private static readonly JsonDocumentOptions WriteOptions = new() { MaxDepth = 64, AllowDuplicateProperties = false };

    /// <summary>
    /// Planı (çözümsüz plan, çözüm) olarak ayırır. Plan boş / nesne değil / <c>solution</c> yoksa plan aynen, çözüm null
    /// döner. Tüm <c>solution</c> anahtarları silinir (yinelenmişse çözüm = sonuncusu, JS <c>JSON.parse</c> gibi);
    /// <c>"solution": null</c> → çözüm null. Ayrıştırılamayan plan: metninde "solution" (büyük/küçük harf duyarsız) geçiyorsa
    /// plan null (kapalı başarısızlık — çözüm içeriyor olabilecek metin ne yanıta, ne manifeste, ne DB'ye gider), geçmiyorsa
    /// aynen. Tüm çağıranlar (çıktı, export, import) bu tek kuralı kullanır. İstisna atmaz.
    /// </summary>
    public static (string? Plan, string? Solution) Split(string? plan)
    {
        if (!TrySplit(plan, ReadOptions, out var cleanPlan, out var solution) && MentionsSolution(plan))
            cleanPlan = null;
        return (cleanPlan, solution);
    }

    /// <summary>
    /// Yanıt için: plan her zaman çözümsüz döner (<see cref="Split"/> kuralı, kapalı başarısızlık dahil); çözüm yalnız
    /// <paramref name="includeSolution"/> true ise döner — kolondaki değer, yoksa (taşınmamış eski satır) plandan ayrılan.
    /// İstisna atmaz.
    /// </summary>
    public static (string? Plan, string? Solution) ForOutput(string? plan, string? storedSolution, bool includeSolution)
    {
        var (cleanPlan, legacySolution) = Split(plan);
        return (cleanPlan, includeSolution ? storedSolution ?? legacySolution : null);
    }

    private static bool MentionsSolution(string? text) =>
        text != null && text.Contains(SolutionKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Yazılacak çözüm boş ya da en çok <see cref="MaxSolutionLength"/> karakterlik, yinelenen anahtarsız bir JSON nesnesi
    /// olmalı (ör. <c>{"placements":[...]}</c>).
    /// </summary>
    public static bool IsValidSolution(string? solution) =>
        string.IsNullOrWhiteSpace(solution) || IsStrictJsonObject(solution, MaxSolutionLength);

    /// <summary>Yazılacak plan boş ya da en çok <see cref="MaxPlanLength"/> karakterlik, yinelenen anahtarsız bir JSON nesnesi olmalı.</summary>
    public static bool IsValidPlan(string? plan) =>
        string.IsNullOrWhiteSpace(plan) || IsStrictJsonObject(plan, MaxPlanLength);

    /// <summary>
    /// Kayıt kuralı: plan çözümsüz saklanır; çözüm = ayrı gelen (boş değilse) ?? plandan ayrılan ?? <paramref name="existingSolution"/>
    /// (güncellemede eski/çözümü bilmeyen istemci kaydettiğinde çözüm kaybolmasın). Plan boşsa ikisi de null.
    /// false (hiçbir şey yazılmaz; çağıran 400 döner): ayrı gelen çözüm geçersiz; plan geçersiz (<see cref="IsValidPlan"/>);
    /// ya da ayrı çözüm yokken plandan ayrılan çözüm geçersiz (nesne değil / sınır aşımı — ayrı alanla aynı kural).
    /// Ayrı çözüm verildiyse plandaki çözüm kullanılmadığı için doğrulanmaz, yalnız plandan silinir.
    /// </summary>
    public static bool TryResolveForWrite(string? plan, string? explicitSolution, string? existingSolution,
        out string? storedPlan, out string? storedSolution)
    {
        storedPlan = null;
        storedSolution = null;
        if (!IsValidSolution(explicitSolution))
            return false;
        if (string.IsNullOrWhiteSpace(plan))
            return true;
        if (!IsValidPlan(plan) || !TrySplit(plan, WriteOptions, out var cleanPlan, out var planSolution))
            return false;

        if (!string.IsNullOrWhiteSpace(explicitSolution))
        {
            storedSolution = explicitSolution.Trim();
        }
        else if (planSolution != null)
        {
            if (!IsValidSolution(planSolution))
                return false;
            storedSolution = planSolution;
        }
        else
        {
            storedSolution = existingSolution;
        }

        storedPlan = cleanPlan;
        return true;
    }

    /// <summary>
    /// false: plan boş değil ve ayrıştırılamadı (çıkış = girdi, çözüm null). true: ayrıştırıldı ya da boş; nesne ve
    /// <c>solution</c> anahtarı varsa anahtarlar silinmiş yeni metin döner, yoksa girdi aynen.
    /// </summary>
    private static bool TrySplit(string? plan, JsonDocumentOptions options, out string? cleanPlan, out string? solution)
    {
        cleanPlan = plan;
        solution = null;
        if (string.IsNullOrWhiteSpace(plan))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(plan, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return true;

            var hasSolution = false;
            JsonElement lastSolution = default;
            foreach (var property in root.EnumerateObject())
            {
                if (!property.NameEquals(SolutionKey))
                    continue;
                hasSolution = true;
                lastSolution = property.Value;
            }
            if (!hasSolution)
                return true;

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (!property.NameEquals(SolutionKey))
                        property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            cleanPlan = Encoding.UTF8.GetString(buffer.ToArray());
            solution = lastSolution.ValueKind == JsonValueKind.Null ? null : lastSolution.GetRawText();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            cleanPlan = plan;
            solution = null;
            return false;
        }
    }

    private static bool IsStrictJsonObject(string json, int maxLength)
    {
        if (json.Length > maxLength)
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json, WriteOptions);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return false;
        }
    }
}
