using System.Globalization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Localization;

namespace ExamApp.Api.Helpers;

/// <summary>
/// auth-api'nin istemciye giden mesajları için JSON sözlüğü + istek kültürü (issue #231; altyapı #184/#181).
/// Sözlük: <c>auth-api/Resources/&lt;alan&gt;.&lt;dil&gt;.json</c>. Kullanım: <c>IStringLocalizer&lt;Messages&gt;</c>.
/// Kültür yalnızca <c>Accept-Language</c>'tan çözülür (login/exchange anonim olduğu için exam API'deki
/// kullanıcı tercihi provider'ı burada anlamsız); yoksa/desteklenmiyorsa varsayılan dil (tr).
/// </summary>
public static class AuthLocalization
{
    public static IServiceCollection AddAuthLocalization(this IServiceCollection services, Action<JsonLocalizationOptions>? configure = null)
    {
        services.AddJsonLocalization(options =>
        {
            options.ResourcesPath = "Resources";
            configure?.Invoke(options);
        });

        services.Configure<RequestLocalizationOptions>(options =>
        {
            var supportedCultures = SupportedLocales.AllCultureNames
                .Select(name => new CultureInfo(name))
                .ToList();

            options.DefaultRequestCulture = new RequestCulture(SupportedLocales.DefaultCultureName);
            options.SupportedCultures = supportedCultures;
            options.SupportedUICultures = supportedCultures;
            options.ApplyCurrentCultureToResponseHeaders = true;

            options.RequestCultureProviders.Clear();
            options.RequestCultureProviders.Add(new NormalizedAcceptLanguageCultureProvider());
        });

        return services;
    }
}
