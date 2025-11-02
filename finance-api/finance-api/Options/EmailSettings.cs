namespace FinanceApi.Options
{
    public class EmailSettings
    {
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = "Finance App";
        public string SmtpServer { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool UseSsl { get; set; } = true;

        public bool IsConfigured()
        {
            return !string.IsNullOrWhiteSpace(FromAddress)
                && !string.IsNullOrWhiteSpace(SmtpServer)
                && !string.IsNullOrWhiteSpace(Username)
                && !string.IsNullOrWhiteSpace(Password);
        }
    }
}
