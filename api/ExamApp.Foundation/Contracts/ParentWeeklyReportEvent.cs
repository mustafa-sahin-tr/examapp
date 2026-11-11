using System;
using System.Collections.Generic;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #1112 — velinin haftalık raporu. Exam API'nin ParentWeeklyReportJob'ı Pazartesi sabahı (İstanbul) planında
/// <c>weekly-report</c> olan her çocuğun her Active velisine bir event yazar; bağlantının "bu hafta gönderildi" işaretiyle AYNI
/// transaction'da. Sayılar veli panelinin haftalık rapor ucuyla aynı hesaptır (<c>ParentWeeklyReportService</c>).
/// BadgeService in-app bildirim + e-posta üretir.
///
/// TEK ALICI / event (bildirim idempotency'si <c>(Type, SourceEventId)</c>, e-posta <c>NotificationEventLog</c>).
/// </summary>
public class ParentWeeklyReportEvent
{
    /// <summary>Idempotency anahtarı — bu (veli, çocuk, hafta) için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>Çocuğun Students.Id'si (derin link <c>/parent?child=&amp;tab=report</c>).</summary>
    public int StudentId { get; set; }

    /// <summary>Öğrencinin kısa görünen adı ("Ad S."); çözülemediyse boş.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;

    /// <summary>Raporun haftasının Pazartesi'si (yerel takvim günü, saat 00:00, Kind=Unspecified).</summary>
    public DateTime WeekStart { get; set; }

    public int QuestionsSolved { get; set; }
    public int Correct { get; set; }
    public int ActiveDays { get; set; }
    public int StudyMinutes { get; set; }
    public int Points { get; set; }
    public int AssignmentsCompleted { get; set; }
    public int AssignmentsOverdue { get; set; }

    public List<ParentWeeklyReportSubject> Subjects { get; set; } = new();
    public List<ParentWeeklyReportTopicDrop> DroppedTopics { get; set; } = new();
    public List<ParentWeeklyReportMockExam> MockExams { get; set; } = new();

    /// <summary>Hafta içinde kazanılan rozetlerin adları.</summary>
    public List<string> Badges { get; set; } = new();

    /// <summary>Öneri anahtarı: <c>noActivity</c>, <c>overdue</c>, <c>reviewTopic</c>, <c>moreDays</c>, <c>keepGoing</c>.</summary>
    public string Suggestion { get; set; } = string.Empty;

    /// <summary><c>reviewTopic</c> önerisinin konusu.</summary>
    public string? SuggestionTopic { get; set; }

    /// <summary>Velinin exam/auth user id'si — Notification.UserId.</summary>
    public int RecipientUserId { get; set; }

    /// <summary>Velinin Keycloak sub'ı; boş olabilir — consumer BadgeService verisinden çözer.</summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;

    /// <summary>Velinin e-postası; veli e-postayı kapattıysa ya da adres çözülemediyse boş → yalnız in-app.</summary>
    public string RecipientEmail { get; set; } = string.Empty;
}

public class ParentWeeklyReportSubject
{
    public string Name { get; set; } = string.Empty;
    public int Answered { get; set; }
    public int AccuracyPercent { get; set; }
}

public class ParentWeeklyReportTopicDrop
{
    public string Name { get; set; } = string.Empty;
    public int AccuracyPercent { get; set; }
    public int PreviousAccuracyPercent { get; set; }
}

public class ParentWeeklyReportMockExam
{
    public string Name { get; set; } = string.Empty;
    public decimal Net { get; set; }
    public int? Rank { get; set; }
    public int? Takers { get; set; }
}
