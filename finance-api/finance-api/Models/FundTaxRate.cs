using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApi.Models
{
    public class FundTaxRate
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string AssetId { get; set; } = string.Empty;

        /// <summary>
        /// Stopaj oranı (yüzde). Örn: 17 => %17
        /// </summary>
        [Range(0, 100)]
        public decimal RatePercent { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("AssetId")]
        public virtual Asset Asset { get; set; } = null!;
    }
}
