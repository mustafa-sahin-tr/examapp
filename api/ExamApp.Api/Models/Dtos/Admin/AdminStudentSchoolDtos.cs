namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// <c>PUT api/admin/students/{id}/school</c> gövdesi (issue #277 madde 8): <c>{ "schoolId": 12 }</c>.
/// Alan zorunludur — eksik/null ise controller yerelleştirilmiş <c>{ message }</c> ile 400 döner (öğrenciyi okulsuz
/// bırakmak bu uçla yapılmaz). <c>[Required]</c> bilinçli olarak yok: [ApiController]'ın otomatik ProblemDetails yanıtı
/// yerelleştirilmiş mesajı gölgelerdi (<see cref="AdminAccountStatusRequestDto"/> ile aynı gerekçe).
/// </summary>
public sealed class AdminStudentSchoolRequestDto
{
    public int? SchoolId { get; init; }
}

/// <summary>Başarılı yanıt: öğrencinin yeni okulu ve değişiklik olup olmadığı (aynı okul → <c>changed = false</c>).</summary>
public sealed class AdminStudentSchoolResponseDto
{
    /// <summary>Student.Id.</summary>
    public int StudentId { get; init; }

    /// <summary>Öğrencinin güncel okulu (Students.SchoolId).</summary>
    public int SchoolId { get; init; }

    /// <summary>Değişiklikten önceki okul; okulsuzdu ise null.</summary>
    public int? PreviousSchoolId { get; init; }

    /// <summary>false → öğrenci zaten bu okuldaydı (idempotent, yan etki ve audit yok).</summary>
    public bool Changed { get; init; }
}
