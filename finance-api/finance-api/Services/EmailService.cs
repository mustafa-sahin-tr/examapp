using System.Net;
using System.Net.Mail;
using System.Text;
using FinanceApi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinanceApi.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IOptions<EmailSettings> options, ILogger<EmailService> logger)
        {
            _settings = options.Value;
            _logger = logger;
        }

        public async Task SendAsync(string recipientEmail, string subject, string htmlBody, string? textBody = null)
        {
            if (!_settings.IsConfigured())
            {
                _logger.LogWarning("Email settings are not configured. Skipping email send to {Recipient}", recipientEmail);
                return;
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_settings.FromAddress, _settings.FromName, Encoding.UTF8),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };

            message.To.Add(recipientEmail);

            if (!string.IsNullOrWhiteSpace(textBody))
            {
                var alternate = AlternateView.CreateAlternateViewFromString(textBody, Encoding.UTF8, "text/plain");
                message.AlternateViews.Add(alternate);
            }

            using var client = new SmtpClient(_settings.SmtpServer, _settings.SmtpPort)
            {
                EnableSsl = _settings.UseSsl
            }; // explicit setup prevents default credentials fallback

            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);

            try
            {
                await client.SendMailAsync(message);
                _logger.LogInformation("Portfolio summary email sent to {Recipient}", recipientEmail);
            }
            catch (SmtpException smtpEx)
            {
                _logger.LogError(smtpEx, "SMTP error while sending email to {Recipient}", recipientEmail);
                throw;
            }
        }
    }
}
