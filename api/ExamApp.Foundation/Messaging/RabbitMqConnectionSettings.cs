using Microsoft.Extensions.Configuration;

namespace ExamApp.Foundation.Messaging;

/// <summary>
/// RabbitMQ bağlantı ayarlarını (<c>RabbitMQ:Host/Username/Password</c>) fail-fast okur (issue #371).
/// Varsayılan (<c>guest</c>/<c>guest</c>) YOKTUR: servis başına least-privilege kullanıcı (#279) zorunludur,
/// eksik konfigürasyon açılışta anlaşılır bir hatayla durdurur. Hata mesajı değer içermez (yalnız anahtar adları).
/// </summary>
public sealed record RabbitMqConnectionSettings(string Host, string Username, string Password)
{
    // Positional record ToString() Password'ü basardı (security Low-1): maskele.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Host = ").Append(Host).Append(", Username = ").Append(Username).Append(", Password = ***");
        return true;
    }

    public static RabbitMqConnectionSettings Require(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var host = configuration["RabbitMQ:Host"];
        var username = configuration["RabbitMQ:Username"];
        var password = configuration["RabbitMQ:Password"];

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "RabbitMQ:Host tanımlı değil. RabbitMQ:Host/Username/Password konfigürasyonu zorunludur (varsayılan kimlik bilgisi yok).");
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "RabbitMQ:Host tanımlı ama RabbitMQ:Username/RabbitMQ:Password eksik. Servis kendi RabbitMQ kullanıcısıyla yapılandırılmalı (varsayılan guest hesabı kullanılmaz).");
        }

        return new RabbitMqConnectionSettings(host, username, password);
    }
}
