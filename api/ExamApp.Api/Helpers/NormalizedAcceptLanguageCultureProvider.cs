using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Localization;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <see cref="AcceptLanguageHeaderRequestCultureProvider"/>'ın sonucunu desteklenen dil
/// listesine indirger (issue #181).
///
/// Neden düz builtin provider yetmiyor: builtin provider header'daki değeri olduğu gibi
/// döner ("tr"), middleware ise onu SupportedCultures (<c>tr-TR</c>, <c>en-US</c>) ile
/// eşleştiremeyip sonucu tamamen atar. Burada bölge eki normalize edilir
/// (<c>tr</c>/<c>tr-TR</c>/<c>tr-CY</c> → <c>tr-TR</c>, <c>en-GB</c> → <c>en-US</c>),
/// böylece istemcinin hangi biçimde gönderdiği önemsiz hâle gelir.
/// Header'daki kalite (q) sıralaması builtin provider'dan miras alınır.
/// </summary>
public class NormalizedAcceptLanguageCultureProvider : AcceptLanguageHeaderRequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var result = await base.DetermineProviderCultureResult(httpContext);
        if (result is null)
        {
            return null;
        }

        foreach (var culture in result.Cultures)
        {
            if (SupportedLocales.TryNormalize(culture.Value, out var locale))
            {
                var cultureName = SupportedLocales.ToCultureName(locale);
                return new ProviderCultureResult(cultureName, cultureName);
            }
        }

        // Header sadece desteklemediğimiz dilleri (veya "*") istiyor — sıradaki provider'a düş.
        return null;
    }
}
