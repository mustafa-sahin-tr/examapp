using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApi.Models
{
    public class Portfolio
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string AssetId { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [Range(0, double.MaxValue)]
        public decimal TotalQuantity { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal AveragePrice { get; set; }

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("AssetId")]
        public virtual Asset Asset { get; set; } = null!;

        public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();

        // Calculated properties
        [NotMapped]
        public decimal CurrentValue => TotalQuantity * (Asset?.CurrentPrice ?? 0);

        [NotMapped]
        public decimal TotalCost => TotalQuantity * AveragePrice;

        [NotMapped]
        public decimal ProfitLoss => CurrentValue - TotalCost;

        [NotMapped]
        public decimal ProfitLossPercentage => TotalCost > 0 ? (ProfitLoss / TotalCost) * 100 : 0;
    }
}
