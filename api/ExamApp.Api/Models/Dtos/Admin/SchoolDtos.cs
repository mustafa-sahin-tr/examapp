namespace ExamApp.Api.Models.Dtos.Admin;

public class SchoolDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ProvinceId { get; set; }
    public string? ProvinceName { get; set; }
    public int? DistrictId { get; set; }
    public string? DistrictName { get; set; }
    public string? AddressLine { get; set; }
}

public class UpsertSchoolDto
{
    public string Name { get; set; } = string.Empty;
    public int? ProvinceId { get; set; }
    public int? DistrictId { get; set; }
    public string? AddressLine { get; set; }
}
