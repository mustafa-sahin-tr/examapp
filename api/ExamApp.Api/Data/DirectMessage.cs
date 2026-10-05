using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>Mesajı gönderen tarafın rolü (issue #106). DB'de string saklanır.</summary>
public enum DirectMessageSenderRole
{
    Student = 0,
    Teacher = 1
}

/// <summary>
/// issue #106: konuşmadaki tek mesaj. Düz metin (HTML render edilmez; <c>CommentBodySanitizer</c> ile kontrol karakterleri
/// reddedilir, bidi/sıfır genişlikli karakterler ayıklanır), 1..<see cref="BodyMaxLength"/> karakter. Gönderim anı
/// <see cref="BaseEntity.CreateTime"/>. <see cref="ReadAt"/> alıcının mesajı ilk okuduğu an (gelen kutusu "okunmamış"
/// filtresi ve dilim (b) rozeti); okundu tiki olarak istemciye gösterilmez (kapsam dışı).
/// </summary>
public class DirectMessage : BaseEntity
{
    public const int BodyMaxLength = 2000;

    [Key]
    public int Id { get; set; }

    public int ConversationId { get; set; }

    [ForeignKey(nameof(ConversationId))]
    public Conversation Conversation { get; set; } = null!;

    /// <summary>Gönderenin exam/auth user id'si.</summary>
    public int SenderUserId { get; set; }

    public DirectMessageSenderRole SenderRole { get; set; }

    [Required, MaxLength(BodyMaxLength)]
    public string Body { get; set; } = string.Empty;

    /// <summary>Alıcının mesajı okuduğu an (UTC); null = okunmamış.</summary>
    public DateTime? ReadAt { get; set; }
}
