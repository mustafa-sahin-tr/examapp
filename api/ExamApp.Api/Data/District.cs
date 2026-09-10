using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// İlçe — <see cref="Province"/>'a bağlı referans tablo (issue #91).
/// (ProvinceId, Name) benzersizdir; "Merkez" gibi isimler iller arasında tekrar edebilir.
/// </summary>
public class District
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public int ProvinceId { get; set; }
    public Province Province { get; set; } = null!;
}
