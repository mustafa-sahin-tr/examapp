using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #326 (D4) — bir yorum moderatör tarafından GİZLENDİĞİNDE exam API tarafından gizleme ile AYNI transaction'da
/// outbox'a yazılır. BadgeService tüketip o yoruma işaret eden bildirimlerin başlık/gövdesini nötr metne çevirir
/// (yazar adı — reşit olmayan öğrenci adı olabilir — bildirimde kalmasın). Unhide için event yoktur: metin nötr kalır.
///
/// Alıcı taşımaz (bildirimleri BadgeService kendi verisinden bulur); yorum gövdesi/ad/e-posta taşınmaz.
/// </summary>
public class WorksheetCommentHiddenEvent
{
    /// <summary>Outbox olayının kimliği (izleme/log için). Idempotency doğal: nötr metne çevirme tekrarında değişmez.</summary>
    public Guid EventId { get; set; }

    /// <summary>Gizlenen yorum.</summary>
    public int CommentId { get; set; }

    /// <summary>Gizlenen yorumun thread kökü (kök gizlendiyse CommentId ile aynı).</summary>
    public int RootCommentId { get; set; }

    public int WorksheetId { get; set; }
}
