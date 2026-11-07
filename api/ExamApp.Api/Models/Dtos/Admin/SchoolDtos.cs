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

    // Issue #898: satırdaki sayılar; silme yalnız üyesi ve bekleyen başvurusu olmayan okulda açılır.
    /// <summary>Okula bağlı öğretmen (<c>Teacher.SchoolId</c>).</summary>
    public int TeacherCount { get; set; }

    /// <summary>Üyeliği doğrulanmış öğrenci.</summary>
    public int StudentCount { get; set; }

    /// <summary>Bekleyen başvuru: doğrulanmamış öğrenci üyeliği + bu okula bağlanmak isteyen, onay bekleyen öğretmen.</summary>
    public int PendingCount { get; set; }
}

public class UpsertSchoolDto
{
    public string Name { get; set; } = string.Empty;
    public int? ProvinceId { get; set; }
    public int? DistrictId { get; set; }
    public string? AddressLine { get; set; }
}
