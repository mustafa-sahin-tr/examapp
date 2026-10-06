using System;
using System.Text.RegularExpressions;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #365 (S2): toplu/aktarım ile oluşturulan sorularda <c>.../question.jpg</c> yanında cevap bloğu kırpılmış
/// <c>.../question-v2.jpg</c> da yüklenir (QuestionService, QuestionTransferJobRunner). UI canvas görünümü (v5) v2'yi
/// eskiden URL üzerinde regex ile türetiyordu; imzalı URL'de yol değiştirilemez (imza bozulur) ve regex query'li
/// URL'de eşleşmez. Bu yüzden v2 adresi sunucuda, UI regex'iyle BİREBİR aynı kuralla türetilir ve ayrıca imzalanır.
/// </summary>
public static partial class QuestionImageVariant
{
    // UI: question-canvas-view-v5.component.ts → url.replace(/question(\.[^/?#]+)?$/i, 'question-v2$1')
    // (JS'de bayraksız $ = girdinin sonu; .NET'te $ sondaki satır sonundan önce de eşleşir → birebir karşılığı \z)
    [GeneratedRegex(@"question(\.[^/?#]+)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuestionFileName();

    /// <summary>Saklanan soru görseli adresinin v2 karşılığı; kural uymuyorsa null (UI <c>imageUrl</c>'e düşer).</summary>
    public static string? ToV2(string? storedImageUrl)
    {
        if (string.IsNullOrEmpty(storedImageUrl))
            return null;
        var v2 = QuestionFileName().Replace(storedImageUrl, m => "question-v2" + m.Groups[1].Value, 1);
        return string.Equals(v2, storedImageUrl, StringComparison.Ordinal) ? null : v2;
    }
}
