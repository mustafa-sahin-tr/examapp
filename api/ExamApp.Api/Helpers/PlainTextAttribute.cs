using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #573 (security review): düz metin alanı — ASCII kontrol karakterleri (\n, \r, \t hariç; DEL dahil) reddedilir.
/// [ApiController] model doğrulaması ihlalde 400 döner, hiçbir şey yazılmaz. null/boş geçerlidir.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PlainTextAttribute : ValidationAttribute
{
    public PlainTextAttribute() : base("The field {0} must not contain control characters.") { }

    public override bool IsValid(object? value) => value is not string s || !ContainsControlCharacters(s);

    /// <summary>\n, \r ve \t dışındaki C0 kontrol karakterleri ile DEL (0x7F).</summary>
    public static bool ContainsControlCharacters(string value)
    {
        foreach (var c in value)
        {
            if ((c < 0x20 && c != '\n' && c != '\r' && c != '\t') || c == '\u007F')
                return true;
        }
        return false;
    }
}
