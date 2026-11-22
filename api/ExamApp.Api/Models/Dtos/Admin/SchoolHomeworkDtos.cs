using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// issue #1282: <c>POST api/homework</c> gövdesi. <c>target</c>: <c>Grade</c> (+ <c>gradeId</c>) | <c>Section</c> (+ <c>sectionId</c>) |
/// <c>Students</c> (+ <c>studentIds</c>, 1–200). Ders ve teslim günü (<c>yyyy-MM-dd</c>, bugünden 180 güne kadar) zorunlu; sayfa
/// ya da not en az biri dolu. Kitap ≤ 120, sayfa ≤ 60, not ≤ 500 karakter.
/// </summary>
public sealed class SchoolHomeworkCreateRequestDto
{
    public string? Target { get; init; }
    public int? GradeId { get; init; }
    public int? SectionId { get; init; }
    public List<int>? StudentIds { get; init; }
    public int? SubjectId { get; init; }
    public string? BookName { get; init; }
    public string? Pages { get; init; }
    public string? Note { get; init; }
    public DateOnly? DueDate { get; init; }
}

/// <summary>issue #1282: öğretmenin ödev satırı (oluşturma yanıtı ve liste). Sayılar öğretmenin güncel okul kapsamındaki öğrencilerdir.</summary>
public sealed class SchoolHomeworkDto
{
    public int Id { get; init; }
    public string Target { get; init; } = string.Empty;
    public string? TargetLabel { get; init; }
    public int SubjectId { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string? BookName { get; init; }
    public string? Pages { get; init; }
    public string? Note { get; init; }
    public DateOnly DueDate { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>Teslim günü geçti (yerel takvim).</summary>
    public bool IsPastDue { get; init; }

    public int StudentCount { get; init; }

    /// <summary>"Yaptım" dedi, öğretmen henüz kontrol etmedi.</summary>
    public int StudentDoneCount { get; init; }

    public int DoneCount { get; init; }
    public int PartialCount { get; init; }
    public int NotDoneCount { get; init; }

    /// <summary>Teslim günü geçmiş, ne öğrenci işareti ne öğretmen kontrolü olan öğrenciler.</summary>
    public int OverdueCount { get; init; }
}

/// <summary>issue #1282: <c>GET api/homework/{id}</c> — ödev ve kapsamdaki öğrencilerin durumu.</summary>
public sealed class SchoolHomeworkDetailDto
{
    public SchoolHomeworkDto Homework { get; init; } = new();
    public List<SchoolHomeworkStudentStatusDto> Students { get; init; } = [];
}

public sealed class SchoolHomeworkStudentStatusDto
{
    public int StudentId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string StudentNumber { get; init; } = string.Empty;
    public DateTime? StudentDoneAt { get; init; }

    /// <summary><c>Done</c> | <c>Partial</c> | <c>NotDone</c>; kontrol edilmediyse null.</summary>
    public string? TeacherStatus { get; init; }

    public string? TeacherNote { get; init; }
    public DateTime? CheckedAt { get; init; }
    public bool IsOverdue { get; init; }
}

/// <summary>
/// issue #1282: <c>PUT api/homework/{id}/students/{studentId}</c> — öğretmen kontrolü. <c>status</c> null → kontrol kaldırılır.
/// Not ≤ 300 karakter.
/// </summary>
public sealed class SchoolHomeworkCheckRequestDto
{
    public string? Status { get; init; }
    public string? Note { get; init; }
}

/// <summary>issue #1282: öğrencinin ödev satırı (<c>GET api/student/homework</c>).</summary>
public sealed class StudentHomeworkDto
{
    public int Id { get; init; }
    public string SubjectName { get; init; } = string.Empty;
    public string? BookName { get; init; }
    public string? Pages { get; init; }
    public string? Note { get; init; }
    public DateOnly DueDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public string TeacherName { get; init; } = string.Empty;
    public DateTime? StudentDoneAt { get; init; }
    public string? TeacherStatus { get; init; }
    public string? TeacherNote { get; init; }
    public bool IsOverdue { get; init; }
}

/// <summary>issue #1282: <c>PUT api/student/homework/{id}/done</c> gövdesi — "yaptım" işareti / geri alma.</summary>
public sealed class StudentHomeworkDoneRequestDto
{
    public bool Done { get; init; }
}
