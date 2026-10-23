using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// issue #335 (#326 D1): yorum moderasyon geçmişi — her gizleme/açma (<see cref="WorksheetCommentModerationAction.Hide"/> /
/// <see cref="WorksheetCommentModerationAction.Unhide"/>) ve reddedilen moderasyon denemesi
/// (<see cref="WorksheetCommentModerationAction.DeniedNotModerator"/>) için bir satır. Yalnızca ekleme yapılır (append-only,
/// <see cref="ParentAccessAudit"/> deseni): <see cref="BaseEntity"/>'den türemez, soft delete / update alanı yoktur; satır yalnız
/// saklama süresi işiyle (<c>WorksheetCommentModerationHistoryRetentionJob</c>, <c>CommentModerationHistory</c> ayarları) silinir.
/// <para>FK yok — yorum (veya worksheet) silinse de olay korunur. <see cref="AdminUserActionLog"/>'u ikame etmez; gizle/aç
/// orada da yazılmaya devam eder (bu tablo nedeni ve kapsam kanıtını ekler).</para>
/// <para>Backfill YOK: geçmiş bu tablonun eklendiği sürümden sonraki işlemlerle başlar; önceki gizlemelerin yalnız son durumu
/// (<see cref="WorksheetComment.HiddenReason"/>) ve <see cref="AdminUserActionLog"/> satırları vardır.</para>
/// <para>PII: reddedilen denemede yalnız id'ler (yorum içeriği / öğrenci adı yok, <see cref="Reason"/> null). Hide/Unhide'da
/// <see cref="Reason"/> moderatörün yazdığı serbest metindir (yorumdaki <see cref="WorksheetComment.HiddenReason"/> ile aynı);
/// kişisel veri taşıyabileceği için geçmiş ucunda admin olmayan moderatöre yalnız kendi olaylarında döner.</para>
/// <para>Erken retlerde (worksheet/yorum varlığı ve kapsamı doğrulanmadan) <see cref="WorksheetCommentId"/> ve
/// <see cref="WorksheetId"/> null yazılır — id'ler yalnız doğrulamadan sonra kaydedilir.</para>
/// <para>Açık konu (takip): henüz hiçbir KVKK silme yolu <see cref="Reason"/>'ı temizlemiyor — yorum ya da kullanıcı silinse
/// de satırlar saklama süresi dolana kadar kalır.</para>
/// </summary>
public class WorksheetCommentModerationEvent
{
    public const int ActorRoleMaxLength = 16;

    public long Id { get; set; }

    /// <summary>Yorumun Id'si (FK yok). Rapor listesi gibi yorum bazlı olmayan ya da erken reddedilen çağrılarda null.</summary>
    public int? WorksheetCommentId { get; set; }

    /// <summary>Worksheet Id'si (kapsam sorguları için; FK yok). Global rapor listesi ve erken reddedilen denemede null.</summary>
    public int? WorksheetId { get; set; }

    /// <summary>Olay türü (string saklanır).</summary>
    public WorksheetCommentModerationAction Action { get; set; }

    /// <summary>İşlemi yapan / deneyen kullanıcının exam/auth user id'si.</summary>
    public int ActorUserId { get; set; }

    /// <summary>Olay anındaki rol (<see cref="WorksheetCommentModerationActorRoles"/>).</summary>
    [MaxLength(ActorRoleMaxLength)]
    public string ActorRole { get; set; } = string.Empty;

    /// <summary>Olay anında aktörün okulu (<c>UserSchoolResolver</c>); okulsuz/belirsizse null.</summary>
    public int? ActorSchoolId { get; set; }

    /// <summary>Yorumun yazıldığı andaki yazar okulu (<see cref="WorksheetComment.AuthorSchoolId"/>) — kapsam kanıtı; bilinmiyorsa null.</summary>
    public int? CommentAuthorSchoolId { get; set; }

    /// <summary>
    /// Hide: yeni gizleme nedeni. Unhide: kaldırılan (önceki) neden — yorumdaki <see cref="WorksheetComment.HiddenReason"/>
    /// temizlense de burada kalır. DeniedNotModerator: her zaman null.
    /// </summary>
    [MaxLength(WorksheetComment.HiddenReasonMaxLength)]
    public string? Reason { get; set; }

    /// <summary>Olay anı (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>Moderasyon olay türleri. Kalıcı değer string'dir; yeniden adlandırma geçmiş kayıtları bozar.</summary>
public enum WorksheetCommentModerationAction
{
    Hide = 1,
    Unhide = 2,

    /// <summary>Moderatör olmayanın gizle/aç/şikayet (admin)/rapor listesi/geçmiş çağrısı (403 NotModerator).</summary>
    DeniedNotModerator = 3
}

/// <summary><see cref="WorksheetCommentModerationEvent.ActorRole"/> değerleri (en fazla 16 karakter).</summary>
public static class WorksheetCommentModerationActorRoles
{
    public const string Student = "Student";
    public const string Teacher = "Teacher";

    /// <summary>Admin rolü de taşıyan öğretmen.</summary>
    public const string AdminTeacher = "AdminTeacher";

    /// <summary>Öğretmen/öğrenci olmayan admin.</summary>
    public const string Admin = "Admin";

    /// <summary>Öğrenci/öğretmen/admin olmayan çağıran (ör. başka bir rol); normalde uçlara rol kapısında takılır.</summary>
    public const string Other = "Other";
}
