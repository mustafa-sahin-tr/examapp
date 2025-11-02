namespace FinanceApi.Services
{
    public interface IEmailService
    {
        Task SendAsync(string recipientEmail, string subject, string htmlBody, string? textBody = null);
    }
}
