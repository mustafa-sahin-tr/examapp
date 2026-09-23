using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Liderlik puan hattı (issue #225): BadgeService'teki <c>StudentQuestionAggregate.TotalPoints</c>
/// (puanın tek doğruluk kaynağı) değiştiğinde BadgeService'in KENDİ outbox'ına (badge DB) aynı
/// SaveChanges içinde yazdığı event. <c>badge-outbox-publisher</c> RabbitMQ'ya taşır; exam API
/// tüketip <c>StudentPoints.XP</c>'yi upsert eder (liderlik tablosu bu kolondan sıralar).
///
/// Payload bilinçli olarak MUTLAK değer taşır (delta değil): tekrar teslim edilen ya da kaybolup
/// sonradan gelen bir event puanı çift saymaz; exam API <see cref="UpdatedAtUtc"/> ile eski bir
/// event'in daha yeni değerin üstüne yazmasını engeller. Hassas veri yok — yalnızca sayısal id ve puan.
/// </summary>
public class StudentPointsChangedEvent
{
    /// <summary>auth-api User.Id (BadgeService aggregate anahtarı). exam API bunu <c>Students.UserId</c> ile eşler.</summary>
    public int UserId { get; set; }

    /// <summary>Öğrencinin güncel toplam puanı (mutlak değer, &gt;= 0).</summary>
    public int TotalPoints { get; set; }

    /// <summary>
    /// Aggregate'in bu değere ulaştığı an (UTC, mikrosaniyeye yuvarlanmış). Versiyon görevi görür:
    /// tüketici yalnızca kayıttaki değerden DAHA YENİ event'leri uygular.
    /// </summary>
    public DateTime UpdatedAtUtc { get; set; }
}
