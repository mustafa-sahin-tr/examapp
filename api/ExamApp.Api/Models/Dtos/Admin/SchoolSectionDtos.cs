namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>issue #1279: şube (liste satırı). <c>StudentCount</c> aktif üyeler.</summary>
public sealed class SchoolSectionDto
{
    public int Id { get; init; }
    public int GradeId { get; init; }
    public string GradeName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int StudentCount { get; init; }
}

/// <summary>issue #1279: şubedeki öğrenci (numara maskeli, #262).</summary>
public sealed class SchoolSectionStudentDto
{
    public int StudentId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string StudentNumber { get; init; } = string.Empty;
    public DateTime Since { get; init; }
}

/// <summary><c>POST api/school-admin/sections</c> gövdesi.</summary>
public sealed class SchoolSectionCreateRequestDto
{
    public int? GradeId { get; init; }
    public string? Name { get; init; }
}

/// <summary><c>PUT api/school-admin/sections/{id}</c> gövdesi (yalnız ad; sınıf değişmez).</summary>
public sealed class SchoolSectionRenameRequestDto
{
    public string? Name { get; init; }
}

/// <summary><c>POST api/school-admin/sections/{id}/students</c> gövdesi: eklenecek / bu şubeye taşınacak öğrenciler (1..100).</summary>
public sealed class SchoolSectionAssignRequestDto
{
    public List<int>? StudentIds { get; init; }
}

/// <summary>Atama yanıtı: <c>assigned</c> yeni eklenen / taşınan, <c>unchanged</c> zaten bu şubede olanlar.</summary>
public sealed class SchoolSectionAssignResponseDto
{
    public int Assigned { get; init; }
    public int Unchanged { get; init; }
}
