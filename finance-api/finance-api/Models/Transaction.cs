using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApi.Models
{
    public class Transaction
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string AssetId { get; set; } = string.Empty;

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public TransactionType Type { get; set; }

        [Required]
        [Range(0.001, double.MaxValue)]
        public decimal Quantity { get; set; }

        [Required]
        [Range(0, double.MaxValue)]
        public decimal Price { get; set; }

        public DateTime Date { get; set; } = DateTime.UtcNow;

        [Range(0, double.MaxValue)]
        public decimal? Fees { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        // Navigation properties
        [ForeignKey("AssetId")]
        public virtual Asset Asset { get; set; } = null!;

        // Calculated properties
        [NotMapped]
        public decimal TotalCost => Quantity * Price + (Fees ?? 0);
    }
}
