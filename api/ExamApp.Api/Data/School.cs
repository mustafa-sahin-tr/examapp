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
}
