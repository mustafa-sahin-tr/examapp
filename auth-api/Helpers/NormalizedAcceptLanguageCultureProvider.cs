using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Localization;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <see cref="AcceptLanguageHeaderRequestCultureProvider"/>'ın sonucunu desteklenen dil listesine indirger
/// (<c>tr</c>/<c>tr-TR</c> → <c>tr-TR</c>, <c>en-GB</c> → <c>en-US</c>). Builtin provider "tr" gibi bölgesiz
/// değeri SupportedCultures (<c>tr-TR</c>, <c>en-US</c>) ile eşleştiremeyip atar.
/// exam API'deki (api/ExamApp.Api/Helpers) aynı adlı sınıfın kopyası — Foundation ASP.NET Core'a bağımlı
/// olmadığı için oraya taşınmadı; davranış değişirse ikisi birlikte güncellenmeli.
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

        return null;
    }
}
