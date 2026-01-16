namespace FinanceApi.Models.Dtos
{
    public class FundTaxRateDto
    {
        public string Id { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string AssetSymbol { get; set; } = string.Empty;
        public decimal RatePercent { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class UpsertFundTaxRateDto
    {
        public decimal RatePercent { get; set; }
    }
}
