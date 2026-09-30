namespace BadgeService.Entities;

/// <summary>
/// issue #326 (D4, security O1): gizlenmiş yorumun işareti. Gizleme event'i, gizlenen yorumun Created/Replied bildirimi
/// yazılmadan ÖNCE işlenirse (retry/sıra bozulması) bildirim yazar adıyla oluşurdu; Created/Replied consumer'ları yazdıktan
/// sonra bu tabloya bakıp metni nötrler. Yalnız id taşır (PII yok); unhide'da silinmez (metin nötr kalır kararıyla tutarlı).
/// </summary>
public class HiddenCommentTombstone
{
    public int CommentId { get; set; }

    public DateTime HiddenAt { get; set; } = DateTime.UtcNow;
}
