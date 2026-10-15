namespace BadgeService.Services;

/// <summary>Gönderilecek e-posta (issue #440). Gövde alanları hassas veri (aktivasyon bağlantısı) taşıyabilir: asla loglanmamalı (Development hariç).</summary>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>
/// E-posta gönderim soyutlaması (issue #440). Gerçek sağlayıcı (SMTP/SendGrid vb.) bu arayüzü uygulayıp
/// <c>Program.cs</c>'teki <see cref="IEmailSender"/> kaydını değiştirerek takılır; consumer'lar değişmez.
/// Hata durumunda exception fırlatır (consumer retry, sonra error kuyruğu); sessizce yutmaz.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
