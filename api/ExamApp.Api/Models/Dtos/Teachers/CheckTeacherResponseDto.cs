using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos.Teachers;

/// <summary>
/// issue #298: <c>GET api/teacher/check-teacher</c> yanıtı. Eskiden <see cref="Teacher"/> entity'si doğrudan dönüyordu
/// (askı nedeni yalnızca <c>[JsonIgnore]</c> ile gizleniyordu); artık yalnızca aşağıdaki alanlar çıkar.
/// Kayıt yoksa yalnızca <c>{ "hasTeacherRecord": false }</c> (eski şekil).
/// </summary>
public class CheckTeacherResponseDto
{
    public bool HasTeacherRecord { get; init; }

    public static CheckTeacherResponseDto NoRecord() => new() { HasTeacherRecord = false };
}

/// <summary>
/// Kayıt varken dönen şekil — üst seviye onay alanları #287/#289 ile aynı (<see cref="TeacherApprovalState"/>).
/// JSON: <c>{ hasTeacherRecord, teacher: {...}, teacherAccountApproved, teacherAccountSuspended,
/// teacherApplicationStatus, rejectionReason }</c>. Askı nedeni, tutor profili, seed bayrağı ve audit alanları YOK.
/// </summary>
public sealed class CheckTeacherRecordResponseDto : CheckTeacherResponseDto
{
    public CheckTeacherTeacherDto Teacher { get; init; } = default!;

    public bool TeacherAccountApproved { get; init; }

    /// <summary>issue #289: hesap onayı askıda. Neden dönmez.</summary>
    public bool TeacherAccountSuspended { get; init; }

    public string TeacherApplicationStatus { get; init; } = string.Empty;

    /// <summary>Yalnızca başvuru Rejected iken dolu (admin'in ret gerekçesi; askı nedeni DEĞİL).</summary>
    public string? RejectionReason { get; init; }

    public static CheckTeacherRecordResponseDto From(Teacher teacher)
    {
        var approval = TeacherApprovalState.From(teacher);
        return new CheckTeacherRecordResponseDto
        {
            HasTeacherRecord = true,
            Teacher = CheckTeacherTeacherDto.From(teacher),
            TeacherAccountApproved = approval.TeacherAccountApproved,
            TeacherAccountSuspended = approval.TeacherAccountSuspended,
            TeacherApplicationStatus = approval.TeacherApplicationStatus,
            RejectionReason = approval.RejectionReason
        };
    }
}

/// <summary>
/// <c>teacher</c> alt nesnesi — UI'ın <c>Teacher</c> modelindeki (ui/src/app/models/teacher.ts) kimlik/okul/tema alanları.
/// </summary>
public sealed class CheckTeacherTeacherDto
{
    public int Id { get; init; }
    public int UserId { get; init; }
    public string? SchoolName { get; init; }
    public int? SchoolId { get; init; }
    public bool IsIndependentTutor { get; init; }
    public string? ThemePreset { get; init; }
    public string? ThemeCustomConfig { get; init; }

    public static CheckTeacherTeacherDto From(Teacher teacher) => new()
    {
        Id = teacher.Id,
        UserId = teacher.UserId,
        SchoolName = teacher.SchoolName,
        SchoolId = teacher.SchoolId,
        IsIndependentTutor = teacher.IsIndependentTutor,
        ThemePreset = teacher.ThemePreset,
        ThemeCustomConfig = teacher.ThemeCustomConfig
    };
}
