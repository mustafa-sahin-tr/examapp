using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// issue #106: öğrenci ↔ öğretmen doğrudan mesajlaşma konuşması. Çift başına TEK sürekli akış (konu başlığı yok):
/// <c>(StudentUserId, TeacherUserId)</c> tekil index'i eşzamanlı ilk mesajda ikinci konuşma açılmasını DB seviyesinde
/// engeller. Konuşmayı yalnız öğrenci başlatır (ilk mesaj oluşturur); öğretmen yeni konuşma başlatamaz. İlişki (okul /
/// atama) sonradan bitse de konuşma iki taraf için okunabilir kalır; yeni mesaj kuralı <c>IDirectMessagePolicy</c>'de.
/// Taraflar exam/auth user id'leriyle tutulur (Student/Teacher satırı yeniden oluşsa da kimlik sabit).
/// </summary>
public class Conversation : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>Öğrencinin exam/auth user id'si (Students.UserId).</summary>
    public int StudentUserId { get; set; }

    /// <summary>Öğretmenin exam/auth user id'si (Teachers.UserId).</summary>
    public int TeacherUserId { get; set; }

    /// <summary>Son mesajın anı (UTC) — gelen kutusu/konuşma listesi sıralaması. Oluşturulduğu anda ilk mesajla dolar.</summary>
    public DateTime LastMessageAt { get; set; }
}
