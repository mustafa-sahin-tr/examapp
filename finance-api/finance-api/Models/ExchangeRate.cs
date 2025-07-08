using System.ComponentModel.DataAnnotations;

namespace FinanceApi.Models
{
    /// <summary>
    /// Döviz kuru bilgileri
    /// </summary>
    public class ExchangeRate
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        [MaxLength(3)]
        public string FromCurrency { get; set; } = string.Empty;

        [Required]
        [MaxLength(3)]
        public string ToCurrency { get; set; } = string.Empty;

        [Required]
        public decimal Rate { get; set; }

        [Required]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public decimal ChangePercentage { get; set; }
        public decimal ChangeValue { get; set; }

        // Calculated property
        public string Symbol => $"{FromCurrency}-{ToCurrency}";
    }

    /// <summary>
    /// Kullanıcının para birimi tercihi
    /// </summary>
    public class UserCurrencyPreference
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(3)]
        public string PreferredCurrency { get; set; } = "TRY";

        [Required]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
