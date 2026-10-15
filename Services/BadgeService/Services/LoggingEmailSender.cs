namespace BadgeService.Services;

/// <summary>
/// Varsayılan <see cref="IEmailSender"/> (issue #440): gerçek sağlayıcı yokken e-postayı göndermez, loglar.
/// Development'ta tam mesaj (aktivasyon URL'si dahil) Information'da loglanır; yerel geliştirmede bağlantıyı log'dan alırsın.
/// Development dışında yalnız "sağlayıcı yapılandırılmamış" uyarısı yazılır; gövde/alıcı/token LOGLANMAZ.
/// </summary>
public sealed class LoggingEmailSender : IEmailSender
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<LoggingEmailSender> _logger;

    public LoggingEmailSender(IHostEnvironment env, ILogger<LoggingEmailSender> logger)
    {
        _env = env;
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (_env.IsDevelopment())
        {
            _logger.LogInformation(
                "[DEV e-posta] To={To} Subject={Subject}{NewLine}{Body}",
                message.To, message.Subject, Environment.NewLine, message.TextBody);
        }
        else
        {
            _logger.LogWarning(
                "No email provider configured (IEmailSender = LoggingEmailSender); e-posta gönderilmedi. Subject={Subject}",
                message.Subject);
        }

        return Task.CompletedTask;
    }
}
