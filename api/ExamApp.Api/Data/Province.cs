using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Türkiye ili — referans tablo (issue #91). Seed ile doldurulur
/// (<see cref="ReferenceDataSeed"/>), uygulama üzerinden düzenlenmez.
/// </summary>
public class Province
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public ICollection<District> Districts { get; set; } = new List<District>();
}
