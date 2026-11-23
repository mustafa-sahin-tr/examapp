using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos.Admin;

/// <summary>
/// Admin dashboard'ın üst özet kartları (Issue #85, Phase 1). Yalnızca anlık toplamlar içerir;
/// zaman serisi / trend alanları Phase 2'de ayrı bir DTO ile eklenecek.
/// </summary>
public class DashboardSummaryDto
{
    public int TeacherCount { get; set; }
    public int StudentCount { get; set; }
    public int WorksheetCount { get; set; }
    public int QuestionCount { get; set; }
    public int AiClassifiedQuestionCount { get; set; }

    /// <summary>AiClassifiedQuestionCount / QuestionCount, 0..1 aralığında. Soru yoksa 0.</summary>
    public double AiClassifiedRatio { get; set; }

    /// <summary>Issue #1315: sınıflandırma kırılımı (AI, kitap, öğretmen, AI çalıştı ama konu yok, sınıflandırılmamış).</summary>
    public ClassificationBreakdownDto Classification { get; set; } = new();

    /// <summary>
    /// Issue #441 hedef kümesi: Active velisi ve aktivasyon bekleyen daveti olmayan öğrenciler (hoşgörü sayacı henüz başlamamış olanlar
    /// dahil). Kayıp riskini izlemek için.
    /// </summary>
    public int StudentsWithoutParent { get; set; }

    /// <summary>issue #887: son yapay zekâ sınıflandırmasının zamanı (AI kaynaklı sorunun son güncellenmesi, UTC); hiç yoksa null.</summary>
    public DateTime? LastAiClassificationAt { get; set; }

    /// <summary>issue #887: hata verip yayımlanamamış (işlenmemiş, hatalı) outbox mesajı — sistem durumu uyarısı.</summary>
    public int FailedOutboxCount { get; set; }

    /// <summary>Issue #441: hedef kümeden hoşgörü süresi içinde olanlar (sayaç başlamış, süre dolmamış).</summary>
    public int StudentsInParentGrace { get; set; }

    /// <summary>Issue #441: hedef kümeden süresi dolmuş olanlar (yalnız "veli bilgisi ekle" ekranını görür).</summary>
    public int StudentsParentGraceExpired { get; set; }

    /// <summary>Issue #440: velisi henüz aktive etmemiş / onaylamamış (açık davetli) öğrenciler — kısıtlı mod.</summary>
    public int StudentsAwaitingParentActivation { get; set; }
}

/// <summary>
/// Issue #1315: soruların sınıflandırma durumu. "Sınıflandırılmış" = ders ve konusu var (soru bankasının <c>classified</c>
/// filtresiyle aynı kural); kaynağa göre AI / Book / Teacher (Human ya da kaynağı boş). Konusuz sorular: kaynağı AI ise
/// sınıflandırıcı çalışmış ama konu bulamamış (<see cref="AiNoTopic"/>), değilse hiç sınıflandırılmamış (<see cref="NotClassified"/>).
/// Yalnız yayındaki sorular sayılır (bkz. <see cref="Unpublished"/>).
/// </summary>
public class ClassificationBreakdownDto
{
    public int Ai { get; set; }
    public int Book { get; set; }
    public int Teacher { get; set; }
    public int AiNoTopic { get; set; }
    public int NotClassified { get; set; }

    /// <summary>
    /// Issue #690 ile yayından kaldırılmış ya da lisansı bitmiş sorular: yukarıdaki beş gruba girmez (soru bankasında görünmez,
    /// yeniden sınıflandırılmaz). Beş grup + bu = <see cref="DashboardSummaryDto.QuestionCount"/>.
    /// </summary>
    public int Unpublished { get; set; }

    /// <summary>(Ai + Book + Teacher) / beş grubun toplamı, 0..1. Yayında soru yoksa 0.</summary>
    public double ClassifiedRatio { get; set; }
}

/// <summary>Issue #1315: "AI ile sınıflandır" / "Yeniden dene" sonucu.</summary>
public class ClassificationRetryResultDto
{
    /// <summary>Bu çağrıda sınıflandırma olayı yazılan soru sayısı (en çok <c>BatchSize</c>).</summary>
    public int Queued { get; set; }

    /// <summary>Gruptaki sınıflandırılabilir sorulardan bu çağrıda kuyruğa alınmayanlar (sonraki çağrıya kalır).</summary>
    public int Remaining { get; set; }
}

/// <summary>Günlük zaman serisinde tek bir nokta (UTC gün).</summary>
public class DailyPointDto
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Admin dashboard trend serileri (Issue #87). Her seri son N gün için günlük, boşluksuz
/// (veri olmayan günler 0) ve tarihe göre artan sıradadır.
/// </summary>
public class DashboardTrendsDto
{
    /// <summary>Gün başına oluşturulan soru sayısı (Question.CreateTime).</summary>
    public List<DailyPointDto> QuestionCreated { get; set; } = new();

    /// <summary>
    /// Gün başına çözülen soru sayısı. PracticeSessionQuestion (AnsweredAt) ve
    /// WorksheetInstanceQuestion (cevaplanmış satırın UpdateTime'ı) toplamı, tek seri.
    /// </summary>
    public List<DailyPointDto> QuestionSolved { get; set; } = new();

    /// <summary>
    /// Gün başına başarılı öğrenci login sayısı (Issue #89). LoginEvent üzerinden Success=true ve
    /// Role=Student (case-insensitive) filtresiyle sayılır; diğer roller bu seriye dahil edilmez.
    /// </summary>
    public List<DailyPointDto> StudentLogin { get; set; } = new();
}
