using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// issue #1281: <c>POST api/announcements</c> gövdesi. <c>target</c>: <c>School</c> | <c>Grade</c> (+ <c>gradeId</c>) |
/// <c>Section</c> (+ <c>sectionId</c>) | <c>Students</c> (+ <c>studentIds</c>, 1–200). Başlık ≤ 100, metin ≤ 500 karakter.
/// </summary>
public sealed class SchoolAnnouncementSendRequestDto
{
    public string? Target { get; init; }
    public int? GradeId { get; init; }
    public int? SectionId { get; init; }
    public List<int>? StudentIds { get; init; }
    public string? Title { get; init; }
    public string? Body { get; init; }
}

/// <summary>issue #1281: gönderilen duyuru (yanıt ve "Gönderilenler" satırı).</summary>
public sealed class SchoolAnnouncementDto
{
    public int Id { get; init; }
    public string Target { get; init; } = string.Empty;
    public string? TargetLabel { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public int StudentCount { get; init; }
    public int TeacherCount { get; init; }
    public int ParentCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>issue #1281: <c>GET api/announcements/options</c> — gönderenin seçebileceği hedefler.</summary>
public sealed class SchoolAnnouncementOptionsDto
{
    public string SchoolName { get; init; } = string.Empty;

    /// <summary>true → okul geneli ve sınıf hedefleri açık (okul yöneticisi).</summary>
    public bool CanTargetSchoolAndGrade { get; init; }
}
