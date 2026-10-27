using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #573 (security review): düz metin alanı — ASCII kontrol karakterleri (\n, \r, \t hariç; DEL dahil) reddedilir.
/// issue #583 (review D3): ayrıca C1 kontrol karakterleri (U+0080–U+009F) ve çift yönlü metin geçersiz kılma / yalıtım
/// karakterleri (U+202A–U+202E, U+2066–U+2069; "Trojan Source" tarzı görüntü sahteciliği) reddedilir.
/// [ApiController] model doğrulaması ihlalde 400 döner, hiçbir şey yazılmaz. null/boş geçerlidir.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PlainTextAttribute : ValidationAttribute
{
    public PlainTextAttribute() : base("The field {0} must not contain control characters.") { }

    public override bool IsValid(object? value) => value is not string s || !ContainsControlCharacters(s);

    /// <summary>
    /// Yasak küme: U+0000–U+0008, U+000B, U+000C, U+000E–U+001F, U+007F–U+009F (DEL + C1), U+202A–U+202E, U+2066–U+2069.
    /// İzinli: \n (U+000A), \r (U+000D), \t (U+0009) ve diğer her karakter.
    /// </summary>
    public static bool ContainsControlCharacters(string value)
    {
        foreach (var c in value)
        {
            if ((c < 0x20 && c != '\n' && c != '\r' && c != '\t')
                || (c >= '\u007F' && c <= '\u009F')
                || (c >= '\u202A' && c <= '\u202E')
                || (c >= '\u2066' && c <= '\u2069'))
                return true;
        }
        return false;
    }
}
