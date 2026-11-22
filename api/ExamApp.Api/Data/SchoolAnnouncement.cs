using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Duyurunun hedefi (issue #1281).</summary>
public enum SchoolAnnouncementTarget
{
    /// <summary>Okulun bütün doğrulanmış öğrencileri + öğretmenleri (yalnız okul yöneticisi).</summary>
    School,

    /// <summary>Okulun bir sınıfındaki doğrulanmış öğrenciler (yalnız okul yöneticisi).</summary>
    Grade,

    /// <summary>Bir şubenin aktif öğrencileri.</summary>
    Section,

    /// <summary>Seçilen öğrenciler (okulun doğrulanmış üyeleri).</summary>
    Students
}

/// <summary>
/// issue #1281 (#1071 S6): okul duyurusu. Gönderildiği okulu damgalar (gönderenin okulu sonradan değişse de satır kalır, gönderen
/// "Gönderilenler"de görmeye devam eder; yeni duyurular güncel okula gider). Alıcı sayıları gönderim anındaki çözümdür; bildirimler
/// BadgeService'te <see cref="ExamApp.Foundation.Contracts.AnnouncementPublishedEvent"/> ile yazılır. Silme / düzenleme yok.
/// </summary>
public class SchoolAnnouncement
{
    public const int TitleMaxLength = 100;
    public const int BodyMaxLength = 500;
    public const int TargetLabelMaxLength = 64;

    public int Id { get; set; }

    public int SchoolId { get; set; }

    public School School { get; set; } = null!;

    public int SenderUserId { get; set; }

    public SchoolAnnouncementTarget Target { get; set; }

    public int? TargetGradeId { get; set; }

    public int? TargetSectionId { get; set; }

    /// <summary>Gönderim anındaki hedef adı (sınıf ya da "sınıf / şube"); okul geneli ve seçili öğrencilerde null.</summary>
    [MaxLength(TargetLabelMaxLength)]
    public string? TargetLabel { get; set; }

    [MaxLength(TitleMaxLength)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(BodyMaxLength)]
    public string Body { get; set; } = string.Empty;

    public int StudentCount { get; set; }

    public int TeacherCount { get; set; }

    public int ParentCount { get; set; }

    public DateTime CreatedAt { get; set; }
}
