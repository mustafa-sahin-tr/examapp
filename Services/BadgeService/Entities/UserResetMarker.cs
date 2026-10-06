using System;

namespace BadgeService.Entities;

/// <summary>
/// issue #396: kullanıcı başına son sıfırlama zamanı. <c>UserResetService</c> sıfırlamayla aynı SaveChanges'te
/// yazar (upsert). Sıfırlamadan önce exam API outbox'ına yazılmış ama sıfırlamadan sonra tüketilen
/// <c>AnswerSubmittedEvent</c>'ler (<c>SubmittedAt</c> &lt; <see cref="ResetAtUtc"/>) <c>AnswerSubmissionAggregationService</c>
/// tarafından yok sayılır — aksi halde sıfırlama <c>AnswerPointAward</c>/<c>ProcessedAnswerSubmission</c>'ı sildiği için
/// soft-delete edilmiş instance'ların puanı yeniden verilirdi.
/// <para>
/// Saat: <see cref="ResetAtUtc"/> exam API'nin saatinden gelir (StudentResetJob gönderir; <c>SubmittedAt</c> ile aynı saat).
/// StudentResetJob önce öğrencinin açık oturumlarını kapatır, sonra çizgiyi alır — çizgiden önce commit eden her cevabın
/// SubmittedAt'i çizginin gerisinde kalır. Çizgi yalnız ileri gider.
/// </para>
/// </summary>
public class UserResetMarker
{
    /// <summary>auth-api User.Id — PK (kullanıcı başına tek satır).</summary>
    public int UserId { get; set; }

    /// <summary>Son sıfırlamanın zamanı (UTC, <c>EventVersion.Normalize</c> ile mikrosaniyeye yuvarlanmış).</summary>
    public DateTime ResetAtUtc { get; set; }
}
