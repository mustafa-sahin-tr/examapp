using System.Globalization;
using ExamApp.Foundation.Localization;
using Microsoft.Extensions.Logging;

namespace BadgeService.Services;

/// <summary>Bir bildirimin lokalize başlık/gövde çifti.</summary>
public record LocalizedNotificationText(string Title, string Body);

/// <summary>
/// Consumer'ların bildirim metnini hedef kültürde üretmesi için tek nokta (issue #185).
///
/// <see cref="JsonStringLocalizer"/> yerine <see cref="JsonResourceStore"/> doğrudan kullanılır:
/// consumer'larda ASP.NET Core istek bağlamı yok, dolayısıyla <c>IStringLocalizer</c>'ın
/// dayandığı <see cref="CultureInfo.CurrentUICulture"/>'a örtük güvenilemez (ve
/// <c>IStringLocalizer.WithCulture</c> .NET 5+'ta kaldırıldı). <see cref="JsonResourceStore.TryGet"/>
/// zaten kültürü açık parametre olarak aldığı için bu, Foundation'a yeni bir arayüz eklemeden
/// çözüyor.
///
/// Anahtar sözleşmesi: <c>notifications.&lt;Type&gt;.title</c> / <c>notifications.&lt;Type&gt;.body</c>
/// (bkz. <c>Services/BadgeService/Resources/README.md</c>). Genel/varsayılan yer tutucu adları
/// (ör. "Bir öğrenci") <c>notifications.common.*</c> altında, <see cref="Resolve"/> ile okunur.
/// </summary>
public interface INotificationTextFactory
{
    /// <summary>
    /// <c>notifications.{type}.title</c> ve <c>notifications.{type}.body</c> anahtarlarını
    /// <paramref name="culture"/> için çözer ve <paramref name="args"/> ile biçimlendirir.
    /// İki anahtar da aynı argüman listesini alır; bir anahtarın kullanmadığı argümanlar
    /// <see cref="string.Format(System.IFormatProvider?,string,object?[])"/> tarafından yok sayılır.
    /// </summary>
    LocalizedNotificationText Build(string type, CultureInfo culture, params object[] args);

    /// <summary>Tek bir anahtarı çözer (varsayılan görünen adlar, ek gövde parçaları vb. için).</summary>
    string Resolve(string key, CultureInfo culture, params object[] args);
}

public class NotificationTextFactory : INotificationTextFactory
{
    private readonly JsonResourceStore _store;
    private readonly ILogger<NotificationTextFactory> _logger;

    public NotificationTextFactory(JsonResourceStore store, ILogger<NotificationTextFactory> logger)
    {
        _store = store;
        _logger = logger;
    }

    public LocalizedNotificationText Build(string type, CultureInfo culture, params object[] args)
    {
        var title = Resolve($"notifications.{type}.title", culture, args);
        var body = Resolve($"notifications.{type}.body", culture, args);
        return new LocalizedNotificationText(title, body);
    }

    public string Resolve(string key, CultureInfo culture, params object[] args)
    {
        var found = _store.TryGet(culture, key, out var template);
        if (!found)
        {
            _logger.LogWarning(
                "Bildirim çeviri anahtarı bulunamadı: '{Key}' (kültür {Culture}). Anahtarın kendisi kullanılacak.",
                key, culture.Name);
        }

        return args is { Length: > 0 } ? string.Format(culture, template, args) : template;
    }
}
