using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Foundation.Localization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Services;

/// <summary>
/// DI dışında oluşturulan consumer'lar (birim testlerde <c>new SomeConsumer(db, hub, logger)</c>)
/// için son çare dil çözücü. Her zaman varsayılan dile (tr) kilitlenir — istek/olay bağlamı
/// olmadan makinenin/ortamın diline göre değişken sonuç üretmesin diye. Üretimde asla kullanılmaz:
/// <c>Program.cs</c> gerçek <see cref="UserLocaleResolver"/>'ı DI ile kaydeder, consumer
/// ctor'larındaki <c>IUserLocaleResolver? localeResolver = null</c> parametresi yalnızca bu
/// güvenli varsayılana düşer (bkz. Foundation'daki <see cref="FallbackMessageLocalizer"/> ile
/// aynı desen).
/// </summary>
public sealed class FallbackUserLocaleResolver : IUserLocaleResolver
{
    public static readonly FallbackUserLocaleResolver Instance = new();

    private FallbackUserLocaleResolver()
    {
    }

    public Task<CultureInfo> ResolveAsync(int userId, string? keycloakId, CancellationToken ct = default)
        => Task.FromResult(CultureInfo.GetCultureInfo(SupportedLocales.DefaultCultureName));
}

/// <summary>
/// <see cref="FallbackUserLocaleResolver"/> ile aynı amaçla: DI dışında oluşturulan consumer'lar
/// için son çare bildirim metni fabrikası. Uygulamanın çıktı klasöründeki <c>Resources</c>
/// sözlüğünü okur ve varsayılan dile (tr) kilitlenir.
/// </summary>
public sealed class FallbackNotificationTextFactory : INotificationTextFactory
{
    private static readonly Lazy<FallbackNotificationTextFactory> LazyInstance = new(Create, isThreadSafe: true);

    private readonly JsonResourceStore _store;
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo(SupportedLocales.DefaultCultureName);

    private FallbackNotificationTextFactory(JsonResourceStore store)
    {
        _store = store;
    }

    /// <summary>Paylaşılan örnek. Kaynak klasörü bulunamazsa anahtarları olduğu gibi döner.</summary>
    public static FallbackNotificationTextFactory Instance => LazyInstance.Value;

    public LocalizedNotificationText Build(string type, CultureInfo culture, params object[] args)
    {
        // culture parametresi kasıtlı yok sayılır — bu tip yalnızca DI dışı (test) yolda
        // kullanılır ve her zaman varsayılan dile kilitlidir (bkz. sınıf yorumu).
        var title = Resolve($"notifications.{type}.title", _culture, args);
        var body = Resolve($"notifications.{type}.body", _culture, args);
        return new LocalizedNotificationText(title, body);
    }

    public string Resolve(string key, CultureInfo culture, params object[] args)
    {
        var found = _store.TryGet(_culture, key, out var template);
        if (!found)
        {
            return key;
        }

        return args is { Length: > 0 } ? string.Format(_culture, template, args) : template;
    }

    private static FallbackNotificationTextFactory Create()
    {
        try
        {
            var root = AppContext.BaseDirectory;
            if (!Directory.Exists(root))
            {
                return new FallbackNotificationTextFactory(JsonResourceStore.Empty);
            }

            using var fileProvider = new PhysicalFileProvider(root);
            var store = JsonResourceStore.Load(fileProvider, "Resources", throwOnDuplicateKeys: false,
                NullLogger.Instance);
            return new FallbackNotificationTextFactory(store);
        }
        catch (Exception)
        {
            return new FallbackNotificationTextFactory(JsonResourceStore.Empty);
        }
    }
}
