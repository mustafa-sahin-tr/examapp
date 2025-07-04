namespace FinanceApi.Models.Dtos
{
    public class TransactionDto
    {
        public string Id { get; set; }
        public string AssetId { get; set; }
        public string Type { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public DateTime Date { get; set; }
        public decimal? Fees { get; set; }
        public string? Notes { get; set; }
    }
}
