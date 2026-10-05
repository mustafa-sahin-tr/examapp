using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// issue #106: öğretmenin bir öğrenciyi doğrudan mesajlaşmada engellemesi (tek yönlü, çift başına). Engel varken öğrenci o
/// öğretmene mesaj gönderemez (nötr 403) ve öğretmen öğrencinin mesajlaşılabilir öğretmen listesinde görünmez; öğretmen
/// kendi cevabını yazmaya devam edebilir. Engel kaldırma = soft-delete (<see cref="BaseEntity.DeleteTime"/>/
/// <see cref="BaseEntity.DeleteUserId"/>); yeniden engel yeni satır açar — satırlar engel geçmişini taşır.
/// Aktif engel çift başına tekildir (<c>NOT IsDeleted</c> filtreli unique index). Her engel/kaldırma ayrıca
/// <see cref="AdminUserActionLog"/>'a yazılır (kim: aktörün sub'ı, kimi: konuşma Id'si, ne zaman).
/// </summary>
public class DirectMessageBlock : BaseEntity
{
    [Key]
    public int Id { get; set; }

    /// <summary>Engelleyen öğretmenin exam/auth user id'si.</summary>
    public int TeacherUserId { get; set; }

    /// <summary>Engellenen öğrencinin exam/auth user id'si.</summary>
    public int StudentUserId { get; set; }
}
