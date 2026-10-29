namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// <c>PUT api/admin/students/{id}/school</c> gövdesi (issue #277 madde 8): <c>{ "schoolId": 12 }</c>.
/// Alan zorunludur — eksikse controller yerelleştirilmiş <c>{ message }</c> ile 400 döner. issue #541: açık
/// <c>{ "schoolId": null }</c> öğrenciyi okulsuz yapar (eksik alan ile ayırt etmek için <see cref="HasSchoolId"/>).
/// <c>[Required]</c> bilinçli olarak yok: [ApiController]'ın otomatik ProblemDetails yanıtı yerelleştirilmiş mesajı
/// gölgelerdi (<see cref="AdminAccountStatusRequestDto"/> ile aynı gerekçe).
/// </summary>
public sealed class AdminStudentSchoolRequestDto
{
    private readonly int? _schoolId;

    public int? SchoolId
    {
        get => _schoolId;
        init
        {
            _schoolId = value;
            HasSchoolId = true;
        }
    }

    /// <summary>Gövdede <c>schoolId</c> alanı vardı (null dahil). System.Text.Json yalnız gelen alanın setter'ını çağırır.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasSchoolId { get; private init; }
}

/// <summary>Başarılı yanıt: öğrencinin yeni okulu ve değişiklik olup olmadığı (aynı okul → <c>changed = false</c>).</summary>
public sealed class AdminStudentSchoolResponseDto
{
    /// <summary>Student.Id.</summary>
    public int StudentId { get; init; }

    /// <summary>Öğrencinin güncel okulu (Students.SchoolId); okulsuz yapıldıysa null (#541).</summary>
    public int? SchoolId { get; init; }

    /// <summary>Değişiklikten önceki okul; okulsuzdu ise null.</summary>
    public int? PreviousSchoolId { get; init; }

    /// <summary>false → öğrenci zaten bu okuldaydı / zaten okulsuzdu (idempotent, yan etki ve audit yok).</summary>
    public bool Changed { get; init; }
}
