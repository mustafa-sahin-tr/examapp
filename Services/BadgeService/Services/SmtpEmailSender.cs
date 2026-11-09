using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace BadgeService.Services;

/// <summary>SMTP ayarları (#1065). <see cref="Host"/> boşsa gönderici kaydedilmez, <see cref="LoggingEmailSender"/> kalır.</summary>
public sealed class SmtpEmailOptions
{
    public const string SectionName = "Email:Smtp";

    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Hedef Okul";
}

/// <summary>
/// E-postayı STARTTLS ile SMTP'den gönderir (#1065). Her gönderim kendi bağlantısını açar; hacim düşük (veli daveti).
/// Hata exception olarak çıkar (consumer retry); alıcı ve gövde burada loglanmaz (<see cref="EmailMessage"/>).
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpEmailOptions _options;

    public SmtpEmailSender(SmtpEmailOptions options)
    {
        _options = options;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.StartTls, ct);
        await client.AuthenticateAsync(_options.Username, _options.Password, ct);
        await client.SendAsync(BuildMessage(message, _options), ct);
        await client.DisconnectAsync(true, ct);
    }

    internal static MimeMessage BuildMessage(EmailMessage message, SmtpEmailOptions options)
    {
        var mime = new MimeMessage { Subject = message.Subject };
        mime.From.Add(new MailboxAddress(options.FromName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }
}

public static class EmailServiceCollectionExtensions
{
    /// <summary>Host doluysa SMTP, boşsa loglayan gönderici. Eksik SMTP ayarı açılışta patlar; ilk davette değil.</summary>
    public static IServiceCollection AddEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(SmtpEmailOptions.SectionName).Get<SmtpEmailOptions>() ?? new SmtpEmailOptions();
        if (string.IsNullOrWhiteSpace(options.Host))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
            return services;
        }

        if (string.IsNullOrWhiteSpace(options.Username)
            || string.IsNullOrWhiteSpace(options.Password)
            || string.IsNullOrWhiteSpace(options.FromAddress))
        {
            throw new InvalidOperationException(
                $"{SmtpEmailOptions.SectionName}: Host is set, so Username, Password and FromAddress are required.");
        }

        services.AddSingleton(options);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        return services;
    }
}
