using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinanceApi.Models
{
    /// <summary>
    /// Kar/Zarar geçmişi - 10 dakikada bir güncellenen snapshot
    /// </summary>
    public class ProfitLossHistory
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Toplam kar/zarar
        [Required]
        public decimal TotalProfitLoss { get; set; }

        [Required]
        public decimal TotalInvestment { get; set; }

        [Required]
        public decimal TotalCurrentValue { get; set; }

        // Yüzde kar/zarar
        [Required]
        public decimal ProfitLossPercentage { get; set; }

        // Asset tipi bazında kar/zarar detayları (JSON olarak saklanacak)
        public string AssetTypeBreakdown { get; set; } = string.Empty;

        // İndex'ler
        [Required]
        public DateTime Date { get; set; } = DateTime.UtcNow.Date;

        [Required]
        public int Hour { get; set; } = DateTime.UtcNow.Hour;
    }

    /// <summary>
    /// Asset tipi bazında kar/zarar detayları
    /// </summary>
    public class AssetTypeProfitLoss
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string ProfitLossHistoryId { get; set; } = string.Empty;

        [Required]
        public AssetType AssetType { get; set; }

        [Required]
        public decimal ProfitLoss { get; set; }

        [Required]
        public decimal Investment { get; set; }

        [Required]
        public decimal CurrentValue { get; set; }

        [Required]
        public decimal ProfitLossPercentage { get; set; }

        [Required]
        public int AssetCount { get; set; }

        // Navigation property
        [ForeignKey("ProfitLossHistoryId")]
        public virtual ProfitLossHistory ProfitLossHistory { get; set; } = null!;
    }

    /// <summary>
    /// Bireysel asset bazında kar/zarar detayları
    /// </summary>
    public class AssetProfitLoss
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string ProfitLossHistoryId { get; set; } = string.Empty;

        [Required]
        public string AssetId { get; set; } = string.Empty;

        [Required]
        public decimal ProfitLoss { get; set; }

        [Required]
        public decimal Investment { get; set; }

        [Required]
        public decimal CurrentValue { get; set; }

        [Required]
        public decimal ProfitLossPercentage { get; set; }

        [Required]
        public decimal Quantity { get; set; }

        [Required]
        public decimal AveragePrice { get; set; }

        [Required]
        public decimal CurrentPrice { get; set; }

        // Navigation properties
        [ForeignKey("ProfitLossHistoryId")]
        public virtual ProfitLossHistory ProfitLossHistory { get; set; } = null!;

        [ForeignKey("AssetId")]
        public virtual Asset Asset { get; set; } = null!;
    }
}
