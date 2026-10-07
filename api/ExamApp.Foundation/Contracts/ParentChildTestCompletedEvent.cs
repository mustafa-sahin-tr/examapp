using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #423 (epic #407 V5) — bir öğrenci testi tamamladığında (Started → Completed koşullu yazımı gerçekten gerçekleştiğinde)
/// exam API'nin <c>TestSessionService.EndTest</c>'i tarafından durum değişikliğiyle AYNI transaction'da, o an Active bağlantısı olan
/// HER veli için bir tane outbox'a yazılır. Öğrencinin velisi yoksa hiçbir şey yazılmaz. Süresi dolan (Expired) test bildirilmez.
///
/// TEK ALICI / event (bkz. <see cref="ParentHomeworkOverdueEvent"/>). Güvenlik: id + Keycloak sub + kısa görünen ad; asla e-posta/token/davet kodu.
/// </summary>
public class ParentChildTestCompletedEvent
{
    /// <summary>Idempotency anahtarı — bu veli için üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>WorksheetInstances.Id.</summary>
    public int TestInstanceId { get; set; }

    public int WorksheetId { get; set; }

    public string WorksheetName { get; set; } = string.Empty;

    public int StudentId { get; set; }

    /// <summary>Öğrencinin kısa görünen adı ("Ad S."); çözülemediyse boş.</summary>
    public string StudentDisplayName { get; set; } = string.Empty;

    public int ParentId { get; set; }

    public int ParentUserId { get; set; }

    /// <summary>Velinin Keycloak sub'ı; boş olabilir (bkz. <see cref="ParentHomeworkOverdueEvent.ParentKeycloakId"/>).</summary>
    public string ParentKeycloakId { get; set; } = string.Empty;

    public int CorrectAnswers { get; set; }

    public int TotalQuestions { get; set; }

    /// <summary>Yüzde puan (0-100): öğrencinin sonuç ekranıyla aynı formül (doğru * 100 / soru sayısı).</summary>
    public int Score { get; set; }

    /// <summary>Tamamlanma anı (UTC).</summary>
    public DateTime CompletedAtUtc { get; set; }
}
