using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>
/// Atama izni akışı (issue #13): sınav+öğretmen çiftine bağlı kalıcı atama yetkisi.
/// Bir talep onaylanınca oluşturulur; sahibi sonradan geri alabilir (<see cref="RevokedAt"/>),
/// ayrıca sınav Private'a çekilince tüm aktif grant'lar iptal edilir.
/// "Aktif grant" = <see cref="RevokedAt"/> == null. Yalnızca atama izni verir; düzenleme izni vermez.
/// </summary>
public class WorksheetAccessGrant : BaseEntity
{
    public int Id { get; set; }

    public int WorksheetId { get; set; }

    [ForeignKey(nameof(WorksheetId))]
    public Worksheet Worksheet { get; set; } = default!;

    /// <summary>Yetki verilen öğretmenin exam/auth user id'si.</summary>
    public int TeacherUserId { get; set; }

    /// <summary>Yetkiyi veren kullanıcının id'si (sahibi veya admin).</summary>
    public int GrantedByUserId { get; set; }

    public DateTime GrantedAt { get; set; }

    /// <summary>Yetki geri alındıysa (veya sınav Private'a çekildiyse) iptal anı (UTC).</summary>
    public DateTime? RevokedAt { get; set; }
}
