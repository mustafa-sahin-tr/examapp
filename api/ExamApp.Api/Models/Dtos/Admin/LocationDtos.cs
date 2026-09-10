namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>İl — cascading dropdown için salt-okunur referans kaydı.</summary>
public class ProvinceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>İlçe — cascading dropdown için salt-okunur referans kaydı.</summary>
public class DistrictDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int ProvinceId { get; set; }
}
