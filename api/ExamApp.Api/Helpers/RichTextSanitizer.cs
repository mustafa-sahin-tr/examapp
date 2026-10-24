using Ganss.Xss;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Soru ve pasaj HTML'i (Quill editör çıktısı) kaydedilmeden önce izin listesiyle temizlenir (#552 security review L2).
/// UI'daki her gösterim Angular sanitizer'ından geçiyor; bu katman Angular dışı tüketiciler (PDF, e-posta, MCP/asistan,
/// mobil) ve editörü atlayıp API'ye doğrudan yazılan içerik için. quill 2.0.3 HTML export XSS'inin (GHSA-v3m3-f69x-jf25)
/// yaması yok — kayıt anında temizlik bu yüzden de gerekli.
/// </summary>
public static class RichTextSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = Create();

    /// <summary>Betik, olay öznitelikleri, javascript:/data: bağlantıları ve izin dışı etiketler atılır; null/boş aynen döner.</summary>
    public static string? Sanitize(string? html)
        => string.IsNullOrEmpty(html) ? html : Sanitizer.Sanitize(html);

    private static HtmlSanitizer Create()
    {
        // Varsayılan izin listesi (biçim etiketleri, güvenli CSS, http/https) Quill'in ürettiğini kapsıyor. class yalnız
        // Quill'in kendi sınıflarıyla: uygulamanın CSS sınıfları (örn. tam ekran katman) içeriğe sokulamasın.
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Add("mailto");
        sanitizer.AllowedAttributes.Add("class");
        foreach (var quillClass in QuillClasses())
        {
            sanitizer.AllowedClasses.Add(quillClass);
        }

        // Quill görselleri satır içi data URI olarak gömer; yalnız <img src> için ve yalnız raster görsel türleri.
        sanitizer.FilterUrl += (_, e) =>
        {
            if (e.SanitizedUrl is null
                && e.Tag.TagName.Equals("IMG", StringComparison.OrdinalIgnoreCase)
                && IsRasterImageDataUri(e.OriginalUrl))
            {
                e.SanitizedUrl = e.OriginalUrl;
            }
        };
        return sanitizer;
    }

    private static IEnumerable<string> QuillClasses()
    {
        foreach (var align in new[] { "center", "right", "justify" })
            yield return $"ql-align-{align}";
        for (var i = 1; i <= 8; i++)
            yield return $"ql-indent-{i}";
        foreach (var size in new[] { "small", "large", "huge" })
            yield return $"ql-size-{size}";
        yield return "ql-font-serif";
        yield return "ql-font-monospace";
        yield return "ql-direction-rtl";
        yield return "ql-syntax";
    }

    private static bool IsRasterImageDataUri(string url)
        => url.StartsWith("data:image/png;", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("data:image/jpeg;", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("data:image/gif;", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("data:image/webp;", StringComparison.OrdinalIgnoreCase);
}
