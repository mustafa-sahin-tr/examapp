using System;

namespace BadgeService.Entities;

/// <summary>
/// "Soru başına bir kez, son cevap sayılır" — issue #279, item 4 (owner kararı). Bir (TestInstanceId,
/// QuestionId) çifti için puanın en son ne kadar uygulandığını tutar; <see cref="AnswerSubmissionAggregationService"/>
/// bunu bir sonraki cevap değişikliğinde ESKİ puanı geri alıp YENİ puanı uygulamak (delta) için okur.
///
/// Sıra/duplicate güvenliği — İKİ modlu (issue #279 review, blocker):
///  - BİRİNCİL: <see cref="LastAppliedRevision"/> — <c>WorksheetInstanceQuestion.AnswerRevision</c>'dan
///    (exam API, DB tarafında atomik artan sayaç) gelen <c>AnswerSubmittedEvent.Revision</c>. İstemci
///    saatine bağlı değildir, iki SaveAnswer çağrısı için ASLA aynı değeri üretmez — bu yüzden
///    <c>SubmittedAt</c>'tan daha güvenilir bir sıralama kaynağıdır. Mesajın Revision'ı &gt; 0 ise KARŞILAŞTIRMA
///    BUNUNLA yapılır: mesaj &lt;= kayıttaki değer ⇒ sırasız/tekrar teslim, atlanır.
///  - YEDEK (yalnız Revision=0 — alan #279'dan ÖNCE üretilmiş/kuyrukta bekleyen eski mesajlar; additive alan,
///    varsayılan 0): <see cref="LastAppliedRevisionUtc"/> (mesajın <c>SubmittedAt</c>'ı,
///    <c>EventVersion.Normalize</c> ile mikrosaniyeye yuvarlanmış) ile karşılaştırılır — #279'un ilk
///    sürümündeki davranış. Bu yolda ayrıca gelecek zaman damgalı (şimdiki zaman + 5 dk'dan ileri) mesajlar
///    reddedilir (log + atla) — istemci saat kaymasının/sahte bir mesajın revizyon korumasını sonsuza kadar
///    kilitlemesini önler.
///  - Her iki modda da UYGULANAN mesajın <c>SubmittedAt</c>'ı <see cref="LastAppliedRevisionUtc"/>'a yazılır
///    (denetim/yedek kıyas için); <see cref="LastAppliedRevision"/> yalnız mesajın Revision'ı &gt; 0 ise
///    güncellenir (0 ile ezilmez — aksi halde revizyon izleyen bir kayıt, sonradan gelen eski-formatlı bir
///    mesajla geriye sıfırlanırdı).
///  - Kullanıcı uyuşmazlığı (issue #279 review): <see cref="UserId"/> mesajınkiyle eşleşmiyorsa (bu
///    (TestInstanceId, QuestionId) başka bir kullanıcıya ait — normalde imkânsız, TestInstanceId zaten tek
///    öğrenciye bağlı, ama savunma amaçlı) mesaj reddedilir/loglanır, HİÇBİR güncelleme yapılmaz.
///
/// <see cref="PointsAwarded"/>: bu (TestInstanceId, QuestionId) için en son uygulanan puan (yanlışsa 0,
/// doğruysa cap'lenmiş <c>QuestionPoint</c>). Bir sonraki mesajda delta = yeniPuan - PointsAwarded hesaplanır
/// ve StudentQuestionAggregate/StudentSubjectAggregate/StudentDailyActivity.TotalPoints'e uygulanır (negatif
/// olabilir — doğru→yanlış puanı düşürür).
///
/// Dağıtım öncesi açık cevaplar (issue #279 review item 3): bu alan eklendiğinde zaten açık olan test
/// instance'larının <c>AnswerRevision</c>'ı 0'dan başlar (yeni kolon default), ilk post-deploy SaveAnswer
/// çağrısı onu 1'e çıkarır — akış otomatik olarak devreye girer, geriye dönük backfill GEREKMEZ. Bu satır
/// (<c>award == null</c>) için "önceki puan" her zaman 0 kabul edilir (ilk cevap zaten böyle davranıyordu).
///
/// Geriye dönük uyumluluk: EventId=Guid.Empty (eski üreticiler) <c>ProcessedAnswerSubmission</c> ledger'ını
/// etkilemez ama BU tablo EventId'den bağımsız çalışır (TestInstanceId/QuestionId anahtarlı) — dolayısıyla
/// EventId'siz mesajlar da (SubmittedAt/Revision doluysa) yukarıdaki korumadan faydalanır.
/// </summary>
public class AnswerPointAward
{
    public int TestInstanceId { get; set; }

    public int QuestionId { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// issue #279 review: <c>AnswerSubmittedEvent.TestInstanceQuestionId</c> — yalnız tanı/hata ayıklama
    /// amaçlı taşınır, anahtarın veya karşılaştırmanın parçası değildir.
    /// </summary>
    public int TestInstanceQuestionId { get; set; }

    /// <summary>
    /// BİRİNCİL sıralama kaynağı (bkz. sınıf XML doc'u). 0 = henüz Revision taşıyan bir mesaj uygulanmadı
    /// (yalnızca <see cref="LastAppliedRevisionUtc"/> yedek modu geçerli).
    /// </summary>
    public int LastAppliedRevision { get; set; }

    /// <summary>YEDEK sıralama kaynağı — en son uygulanan mesajın <c>SubmittedAt</c>'ı (mikrosaniyeye yuvarlanmış).</summary>
    public DateTime LastAppliedRevisionUtc { get; set; }

    /// <summary>Bu (TestInstanceId, QuestionId) için şu an aggregate'lere yansımış puan (&gt;= 0).</summary>
    public int PointsAwarded { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
