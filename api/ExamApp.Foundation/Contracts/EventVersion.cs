using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Zaman damgası tabanlı event versiyonlarının TEK normalizasyon noktası (issue #225): üretici
/// (BadgeService outbox) ve tüketici (exam API upsert) aynı kodu kullanır ki karşılaştırma iki
/// tarafta da aynı değeri görsün.
/// </summary>
public static class EventVersion
{
    /// <summary>
    /// UTC'ye çevirir (Unspecified → UTC kabul edilir) ve mikrosaniyeye yuvarlar (aşağı). PostgreSQL
    /// timestamptz mikrosaniye tutar; yuvarlanmamış 100ns tick, aynı event'in tekrarında versiyonun
    /// "daha yeni" görünmesine yol açardı.
    /// </summary>
    public static DateTime Normalize(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
    }
}
