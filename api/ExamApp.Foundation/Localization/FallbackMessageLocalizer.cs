using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Localization;

namespace ExamApp.Foundation.Localization;

/// <summary>
/// DI dışında oluşturulan nesneler (ör. birim testlerde <c>new SomeService(ctx)</c>) için
/// son çare localizer. Uygulamanın çıktı klasöründeki <c>Resources</c> sözlüğünü okur ve
/// <b>varsayılan dile</b> (<see cref="SupportedLocales.Default"/>) kilitlenir —
/// istek bağlamı olmadığında <c>CurrentUICulture</c> makinenin diline göre değişip
/// deterministik olmayan sonuç üretmesin diye.
///
/// <para>Üretimde bu tip asla kullanılmaz: <c>AddJsonLocalization</c> gerçek localizer'ı
/// kaydeder ve istek kültürünü kullanır. Servis ctor'larındaki
/// <c>IStringLocalizer&lt;Messages&gt;? localizer = null</c> parametresi yalnızca bu güvenli
/// varsayılana düşer.</para>
/// </summary>
public sealed class FallbackMessageLocalizer : JsonStringLocalizer, IStringLocalizer<Messages>
{
    private static readonly Lazy<FallbackMessageLocalizer> LazyInstance = new(Create, isThreadSafe: true);

    private FallbackMessageLocalizer(JsonResourceStore store)
        : base(store)
    {
    }

    /// <summary>Paylaşılan örnek. Kaynak klasörü bulunamazsa anahtarları olduğu gibi döner.</summary>
    public static FallbackMessageLocalizer Instance => LazyInstance.Value;

    protected override CultureInfo? Culture { get; } = new CultureInfo(SupportedLocales.DefaultCultureName);

    private static FallbackMessageLocalizer Create()
    {
        try
        {
            var root = AppContext.BaseDirectory;
            if (!Directory.Exists(root))
            {
                return new FallbackMessageLocalizer(JsonResourceStore.Empty);
            }

            using var fileProvider = new PhysicalFileProvider(root);
            // Fallback yolunda çift anahtar için patlamıyoruz: burada amaç "hiç mesaj yok"a
            // düşmemek; asıl doğrulama uygulama açılışında AddJsonLocalization ile yapılıyor.
            var store = JsonResourceStore.Load(fileProvider, "Resources", throwOnDuplicateKeys: false);
            return new FallbackMessageLocalizer(store);
        }
        catch (Exception)
        {
            return new FallbackMessageLocalizer(JsonResourceStore.Empty);
        }
    }
}
