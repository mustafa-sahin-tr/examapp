namespace FinanceApi.Models.Dtos
{
    public class EmailSummaryRequestDto
    {
        public string RecipientEmail { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string? Message { get; set; }
        public string UserId { get; set; } = "default-user";
    }
}
