using Microsoft.Extensions.FileProviders;

namespace ExamApp.Foundation.Localization;

/// <summary>
/// <see cref="JsonLocalizationServiceCollectionExtensions.AddJsonLocalization"/> ayarları.
/// </summary>
public sealed class JsonLocalizationOptions
{
    /// <summary>
    /// Çeviri dosyalarının ContentRoot'a göre yolu. Altındaki tüm alt klasörler taranır;
    /// dosya adı deseni <c>&lt;alan&gt;.&lt;dil&gt;.json</c> (ör. <c>questions.tr.json</c>).
    /// </summary>
    public string ResourcesPath { get; set; } = "Resources";

    /// <summary>
    /// Aynı anahtar iki farklı dosyada tanımlıysa uygulama başlarken hata fırlatılsın mı?
    /// Varsayılan <c>true</c>: sessizce birinin diğerini ezmesi, paralel çalışan ekiplerde
    /// fark edilmesi çok zor bir hataya dönüşür.
    /// </summary>
    public bool ThrowOnDuplicateKeys { get; set; } = true;

    /// <summary>
    /// Dosyaların okunacağı sağlayıcı. <c>null</c> ise <c>IHostEnvironment.ContentRootFileProvider</c>
    /// kullanılır. Test/gömülü senaryolar için elle verilebilir.
    /// </summary>
    public IFileProvider? FileProvider { get; set; }
}
