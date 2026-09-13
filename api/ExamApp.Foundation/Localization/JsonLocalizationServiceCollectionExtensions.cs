using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ExamApp.Foundation.Localization;

/// <summary>
/// JSON tabanlı mesaj sözlüğünün DI kaydı (issue #184). resx yerine JSON seçildi: yeni bir dil
/// eklemek ya da bir metni düzeltmek yalnızca çeviri dosyasını değiştirmeyi gerektirsin,
/// kod/derleme değişikliği gerekmesin (UI tarafı da JSON kullanıyor).
/// </summary>
public static class JsonLocalizationServiceCollectionExtensions
{
    /// <summary>
    /// <c>Resources/**/&lt;alan&gt;.&lt;dil&gt;.json</c> dosyalarını okuyup
    /// <c>IStringLocalizer&lt;Messages&gt;</c>, <c>IStringLocalizer</c> ve
    /// <see cref="IStringLocalizerFactory"/> olarak kaydeder. Sözlük uygulama açılışında
    /// bir kez okunur; çalışırken dosya izleme/yeniden yükleme yoktur.
    /// </summary>
    public static IServiceCollection AddJsonLocalization(
        this IServiceCollection services,
        Action<JsonLocalizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new JsonLocalizationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        services.AddSingleton(sp =>
        {
            var fileProvider = options.FileProvider
                ?? sp.GetRequiredService<IHostEnvironment>().ContentRootFileProvider;
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(JsonResourceStore));

            return JsonResourceStore.Load(
                fileProvider,
                options.ResourcesPath,
                options.ThrowOnDuplicateKeys,
                logger);
        });

        services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(JsonStringLocalizer<>));
        services.AddSingleton<IStringLocalizer>(sp =>
            sp.GetRequiredService<IStringLocalizerFactory>().Create(typeof(Messages)));

        // Sözlük lazy singleton olduğu için hatalı/çift anahtarlı bir dosya ilk isteğe kadar
        // fark edilmezdi; bu hosted service açılışta yükleyip fail-fast sağlar.
        services.AddHostedService<JsonLocalizationStartupValidator>();

        return services;
    }

    private sealed class JsonLocalizationStartupValidator : IHostedService
    {
        private readonly JsonResourceStore _store;
        private readonly ILogger<JsonLocalizationStartupValidator> _logger;

        public JsonLocalizationStartupValidator(
            JsonResourceStore store,
            ILogger<JsonLocalizationStartupValidator> logger)
        {
            _store = store;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "JSON mesaj sözlüğü yüklendi. Diller: {Cultures}",
                string.Join(", ", _store.Cultures));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
