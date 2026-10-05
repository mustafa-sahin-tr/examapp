using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// "Günün soruları" (issue #99): öğrenci + yerel gün (Europe/Istanbul, <c>ILocalDayCalendar</c>) başına TEK sabit soru seti.
/// İlk istekte tembel üretilir (job yok); (StudentId, Day) unique index'i eşzamanlı ilk isteklerde ikinci set oluşmasını
/// engeller. Çözme mevcut pratik akışıyla yapılır: <see cref="PracticeSessionId"/> setin pratik oturumudur (öğrenci
/// "Başla" deyince açılır), oturumun <c>next</c> ucu set sorularını <see cref="DailyQuestionSetItem.Order"/> sırasıyla verir.
/// <para>
/// Genişleme noktaları: <see cref="ScopeJson"/> #66'nın kullanıcı seçimli kapsamı için (null = sınıftan rastgele, #99);
/// öğrenci+gün tekil kaydı ileride günlük ücretsiz hak/kota için de kullanılabilir.
/// </para>
/// </summary>
public class DailyQuestionSet : BaseEntity
{
    public int Id { get; set; }

    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public Student Student { get; set; } = default!;

    /// <summary>Setin ait olduğu yerel takvim günü (saat bilgisi yok; PostgreSQL <c>date</c>).</summary>
    public DateOnly Day { get; set; }

    /// <summary>Set üretildiğinde öğrencinin sınıfı; gün içinde sınıf değişse de set ve oturumu bu sınıfla kalır.</summary>
    public int GradeId { get; set; }

    [ForeignKey(nameof(GradeId))]
    public Grade Grade { get; set; } = default!;

    /// <summary>Üretim anındaki hedef soru sayısı (config N). Havuz azsa <see cref="Items"/> sayısı bundan küçüktür.</summary>
    public int TargetCount { get; set; }

    /// <summary>#66: kullanıcı seçimli kapsam (ders/konu), JSON. #99'da her zaman null (sınıftan rastgele).</summary>
    public string? ScopeJson { get; set; }

    /// <summary>Setin pratik oturumu; öğrenci başlatana kadar null.</summary>
    public int? PracticeSessionId { get; set; }

    [ForeignKey(nameof(PracticeSessionId))]
    public PracticeSession? PracticeSession { get; set; }

    public ICollection<DailyQuestionSetItem> Items { get; set; } = new List<DailyQuestionSetItem>();
}
