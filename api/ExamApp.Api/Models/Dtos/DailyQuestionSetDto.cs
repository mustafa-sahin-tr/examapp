using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

/// <summary>"Günün soruları" set durumu değerleri (issue #99) — <see cref="DailySetDto.Status"/> bu metinlerden biridir.</summary>
public static class DailySetStatus
{
    /// <summary>Set var, henüz cevaplanmış soru yok (oturum açılmış olabilir).</summary>
    public const string NotStarted = "NotStarted";

    /// <summary>En az bir soru cevaplandı/pas geçildi, set bitmedi.</summary>
    public const string InProgress = "InProgress";

    /// <summary>Setin tüm soruları cevaplandı/pas geçildi; aynı gün yeniden çözülemez.</summary>
    public const string Completed = "Completed";

    /// <summary>Havuzda soru yok (ya da öğrencinin sınıfı tanımlı değil); set üretilmedi.</summary>
    public const string Empty = "Empty";
}

/// <summary>
/// GET api/practice/daily cevabı (issue #99). Set yoksa ilk istekte üretilir.
/// </summary>
public class DailySetDto
{
    /// <summary>Setin yerel günü (Europe/Istanbul), "yyyy-MM-dd".</summary>
    public DateOnly Date { get; set; }

    /// <summary><see cref="DailySetStatus"/>: NotStarted | InProgress | Completed | Empty.</summary>
    public string Status { get; set; } = DailySetStatus.Empty;

    /// <summary>Setteki gerçek soru sayısı (havuz azsa &lt; <see cref="TargetCount"/>; Empty'de 0).</summary>
    public int Total { get; set; }

    /// <summary>Config'teki günlük hedef N (<c>DailyQuestions:QuestionCount</c>).</summary>
    public int TargetCount { get; set; }

    /// <summary>Cevaplanan + pas geçilen soru sayısı (ilerleme "answered / total").</summary>
    public int Answered { get; set; }

    public int Correct { get; set; }

    /// <summary>Cevaplanıp yanlış olan (pas hariç).</summary>
    public int Wrong { get; set; }

    /// <summary>Pas geçilen. <c>Answered = Correct + Wrong + Skipped</c>.</summary>
    public int Skipped { get; set; }

    /// <summary>Setin pratik oturumu; "Başla" denmeden null. Completed'da da döner (review için).</summary>
    public int? SessionId { get; set; }

    /// <summary>#66 kullanıcı seçimli kapsam; #99'da her zaman null (sınıftan rastgele).</summary>
    public DailySetScopeDto? Scope { get; set; }
}

/// <summary>#66 için ayrılmış kapsam şekli (ders/konu id'leri). #99 doldurmaz.</summary>
public class DailySetScopeDto
{
    public List<int> SubjectIds { get; set; } = new();
    public List<int> TopicIds { get; set; } = new();
}

/// <summary>
/// POST api/practice/daily/start cevabı (issue #99). Idempotent: oturum varsa onu döner. Tamamlanmış sette yeni oturum
/// açılmaz; mevcut oturum id'si <c>Status = Completed</c> ile döner (UI bitiş/review ekranına gider).
/// </summary>
public class DailyStartResultDto
{
    public int SessionId { get; set; }

    /// <summary><see cref="DailySetStatus"/>: NotStarted | InProgress | Completed (Empty bu uçta 409 döner).</summary>
    public string Status { get; set; } = DailySetStatus.NotStarted;
}
