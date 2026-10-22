using System;

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

    /// <summary>#66: öğrencinin başlatırken seçtiği kapsam; seçim yapılmadıysa null (sınıftan rastgele, #99).</summary>
    public DailySetScopeDto? Scope { get; set; }
}

/// <summary>
/// #66: günlük setin kaydedilmiş kapsamı (ders, isteğe bağlı konu/alt konu). Gün içinde değişmez; adlar okuma anında çözülür
/// (silinmiş taksonomi kaydında null).
/// </summary>
public class DailySetScopeDto
{
    public int SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int? TopicId { get; set; }
    public string? TopicName { get; set; }
    public int? SubTopicId { get; set; }
    public string? SubTopicName { get; set; }

    /// <summary>Üretimde kapsamdan gelen soru sayısı.</summary>
    public int ScopedCount { get; set; }

    /// <summary>Kapsam yetmedi, eksik kısım sınıf havuzundan tamamlandı ("kalanı genel havuzdan" bilgisi).</summary>
    public bool FilledFromGeneral { get; set; }
}

/// <summary>
/// POST api/practice/daily/start gövdesi (#66, isteğe bağlı). Boş/null: #99 davranışı (rastgele set). Kapsam yalnız setin
/// oturumu henüz açılmamışken doğrulanır ve uygulanır; açılmış sette yok sayılır (gün içinde değişmez). Geçersiz id
/// (≤ 0 dahil) servis doğrulamasında yerelleştirilmiş 400 ile reddedilir.
/// </summary>
public class DailyStartRequestDto
{
    public int? SubjectId { get; set; }

    /// <summary>Doluysa <see cref="SubjectId"/> da dolu olmalı; konu o derse ve öğrencinin sınıfına ait olmalı.</summary>
    public int? TopicId { get; set; }

    /// <summary>Doluysa <see cref="TopicId"/> da dolu olmalı; alt konu o konuya ait olmalı.</summary>
    public int? SubTopicId { get; set; }
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

    /// <summary>Setteki (canlı) soru sayısı; kapsamla yeniden üretilen sette GET'teki değerden farklı olabilir (#66).</summary>
    public int Total { get; set; }

    /// <summary>Setin kaydedilmiş kapsamı (#66); rastgele sette null.</summary>
    public DailySetScopeDto? Scope { get; set; }
}
