using System;

namespace BadgeService.Entities;

/// <summary>
/// "Soru başına bir kez, son cevap sayılır" — issue #279, item 4 (owner kararı). Bir (TestInstanceId,
/// QuestionId) çifti için puanın en son ne kadar uygulandığını tutar; <see cref="AnswerSubmissionAggregationService"/>
/// bunu bir sonraki cevap değişikliğinde ESKİ puanı geri alıp YENİ puanı uygulamak (delta) için okur.
///
/// Sıra/duplicate güvenliği: <see cref="LastAppliedRevisionUtc"/> = uygulanan mesajın
/// <c>AnswerSubmittedEvent.SubmittedAt</c>'ı (TestSessionService.SaveAnswer'ın her çağrıda ürettiği "cevabın
/// güncellenme anı" — ayrı bir revizyon/sequence alanı eklemeye gerek kalmadı). Gelen mesajın
/// <c>SubmittedAt</c>'ı kayıttakinden DAHA YENİ değilse (&lt;=) mesaj sırasız/tekrar teslim edilmiş sayılır ve
/// TÜM aggregate güncellemesi (yalnızca puan değil) atlanır — bkz. <see cref="AnswerSubmissionAggregationService"/>.
///
/// <see cref="PointsAwarded"/>: bu (TestInstanceId, QuestionId) için en son uygulanan puan (yanlışsa 0,
/// doğruysa cap'lenmiş <c>QuestionPoint</c>). Bir sonraki mesajda delta = yeniPuan - PointsAwarded hesaplanır
/// ve StudentQuestionAggregate/StudentSubjectAggregate/StudentDailyActivity.TotalPoints'e uygulanır (negatif
/// olabilir — doğru→yanlış puanı düşürür).
///
/// Geriye dönük uyumluluk: EventId=Guid.Empty (eski üreticiler) bu tabloyu ETKİLEMEZ — dedup EventId'siz
/// zaten atlanıyordu (bkz. ProcessedAnswerSubmission); bu tablo TestInstanceId/QuestionId'ye göre çalışır ve
/// EventId'den bağımsızdır, dolayısıyla eski mesajlar da (SubmittedAt doluysa) revizyon korumasından
/// otomatik faydalanır.
/// </summary>
public class AnswerPointAward
{
    public int TestInstanceId { get; set; }

    public int QuestionId { get; set; }

    public int UserId { get; set; }

    /// <summary>En son uygulanan mesajın <c>AnswerSubmittedEvent.SubmittedAt</c>'ı (revizyon/versiyon).</summary>
    public DateTime LastAppliedRevisionUtc { get; set; }

    /// <summary>Bu (TestInstanceId, QuestionId) için şu an aggregate'lere yansımış puan (&gt;= 0).</summary>
    public int PointsAwarded { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
