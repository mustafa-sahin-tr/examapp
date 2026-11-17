using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.BookImport;

/// <summary>#1219: bir QR çözüm sayfasındaki tek sorunun çözümü (soru sırasıyla).</summary>
public sealed record QrSolution(int Order, Uri? Audio);

/// <summary>
/// #1219: yayıncıya özgü QR çözüm sayfası okuyucusu. Yalnız <see cref="Handles"/> doğru dönen alan adlarına gidilir; okuyucusu
/// olmayan bağlantı çekilmez (onay ekranında yalnız gösterilir).
/// </summary>
public interface IQrKeyReader
{
    bool Handles(Uri uri);

    /// <summary>Çözümler soru sırasıyla; sayfada çözüm listesi yoksa boş.</summary>
    Task<IReadOnlyList<QrSolution>> ReadAsync(Uri link, QrSolutionClient client, CancellationToken ct);
}

/// <summary>
/// Fernus video çözümleri (Fi Yayınları / Define, Karekök …): QR <c>fi.frns.in/&lt;kod&gt;</c> → <c>fideocozum.com/video.php?qr=…</c>;
/// sayfa <c>var solved_list = '[…]'</c> içinde soru başına ses (mp3) + kalem izi + soru görseli ya da mp4 / YouTube taşır. Cevap
/// harfi verilerde yok, öğretmen kaydın sonunda söylüyor ("B seçeneğini işaretleyebiliriz"): ses yazıya çevrilir.
/// </summary>
public sealed partial class FernusQrKeyReader : IQrKeyReader
{
    private const int MaxPageBytes = 2 * 1024 * 1024;

    public bool Handles(Uri uri) => IsFernusHost(uri.Host);

    internal static bool IsFernusHost(string host) =>
        host.Equals("frns.in", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".frns.in", StringComparison.OrdinalIgnoreCase)
        || host.Equals("fideocozum.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("www.fideocozum.com", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<QrSolution>> ReadAsync(Uri link, QrSolutionClient client, CancellationToken ct)
    {
        var html = Encoding.UTF8.GetString(await client.GetAsync(link, u => Handles(u), MaxPageBytes, ct));
        return Parse(html);
    }

    [GeneratedRegex(@"var\s+solved_list\s*=\s*'(?<json>.*?)'\s*;", RegexOptions.Singleline)]
    private static partial Regex SolvedList();

    internal static IReadOnlyList<QrSolution> Parse(string html)
    {
        if (SolvedList().Match(html) is not { Success: true } match)
            return [];
        List<FernusItem>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<FernusItem>>(match.Groups["json"].Value.Replace("\\'", "'"));
        }
        catch (JsonException)
        {
            return [];
        }

        return (items ?? [])
            .Select((item, index) => (item, order: int.TryParse(item.Order, out var o) ? o : index + 1))
            .OrderBy(x => x.order)
            .Select((x, index) => new QrSolution(index + 1, AudioOf(x.item)))
            .ToList();
    }

    /// <summary>Fernus çözümünde ayrı mp3; mp4 çözümde videonun sesi. YouTube ve bilinmeyen tür: ses yok (harf "?").</summary>
    private static Uri? AudioOf(FernusItem item)
    {
        var url = item.SolvedType switch
        {
            "fernus" => item.Audio,
            "mp4" => item.Video,
            _ => null,
        };
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsFernusHost(uri.Host) ? uri : null;
    }

    private sealed class FernusItem
    {
        [JsonPropertyName("order")] public string? Order { get; set; }
        [JsonPropertyName("solved_type")] public string? SolvedType { get; set; }
        [JsonPropertyName("audio")] public string? Audio { get; set; }
        [JsonPropertyName("video")] public string? Video { get; set; }
    }
}

/// <summary>#1219: çözüm kaydının yazısından cevap harfi: son "X seçeneği / X şıkkı / cevap(ımız) X"; bulunamazsa '?'.</summary>
public static partial class QrAnswerLetter
{
    [GeneratedRegex(@"(?<!\p{L})(?<l>[A-E])\s*(?:seçen|şık|sıkk|şik)|\bcevab\w*\s+(?<l>[A-E])\b|\bcevap\s+(?<l>[A-E])\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Letter();

    public static char From(string? transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
            return '?';
        var matches = Letter().Matches(transcript);
        return matches.Count == 0 ? '?' : char.ToUpperInvariant(matches[^1].Groups["l"].Value[0]);
    }
}
