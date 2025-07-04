using System.ComponentModel.DataAnnotations;

namespace FinanceApi.Models
{
    public class Asset
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(10)]
        public string Symbol { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public AssetType Type { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal CurrentPrice { get; set; }

        [Required]
        [MaxLength(10)]
        public string Currency { get; set; } = string.Empty;

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public decimal ChangePercentage { get; set; }

        public decimal ChangeValue { get; set; }

        // Navigation properties
        public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
        public virtual ICollection<Portfolio> Portfolios { get; set; } = new List<Portfolio>();
    }
}
