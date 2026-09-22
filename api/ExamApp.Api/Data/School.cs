using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public class School : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; }

    // Adres (issue #91): il/ilçe normalize referans tablolara FK, açık adres serbest metin.
    // Hepsi nullable — mevcut kayıtlar için geriye dönük uyumlu.
    public int? ProvinceId { get; set; }
    public Province? Province { get; set; }

    public int? DistrictId { get; set; }
    public District? District { get; set; }

    [MaxLength(500)]
    public string? AddressLine { get; set; }

    /// <summary>
    /// Dış sistemdeki kimlik — MEB kurum kodu (issue #216). Yalnızca içe aktarılan okullarda dolu;
    /// elle açılan okullarda null. Dolu olduğunda benzersizdir (filtreli unique index) ve
    /// tekrar çalıştırılan içe aktarmanın kopya üretmemesini sağlayan anahtardır.
    /// </summary>
    [MaxLength(32)]
    public string? ExternalCode { get; set; }

    /// <summary>
    /// Kayıt test verisi aracı (<c>seed-schools</c>) tarafından mı oluşturuldu? Elle açılan
    /// okullardan ayırt etmek ve gerekirse toplu temizlemek için (issue #216).
    /// </summary>
    public bool IsSeedData { get; set; }
}
