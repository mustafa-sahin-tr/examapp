using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Admin'in bir kullanıcı hesabı üzerinde yaptığı yönetim aksiyonunun kaydı (issue #156; <see cref="AdminDataAccessLog"/>
/// (#246) ile aynı desen). <see cref="BaseEntity"/>'den türemez, soft delete yoktur; satır yan etkiden ÖNCE
/// <see cref="AdminUserActionOutcome.Requested"/> olarak yazılır ve yalnızca <see cref="Outcome"/> sonradan güncellenir.
/// Kullanıcı FK'sı yok (<see cref="LoginEvent"/> ile aynı gerekçe).
///
/// Bilinçli olarak sır ve PII içermez: aktör Keycloak <c>sub</c>, hedef yalnızca exam DB id'si + türü.
/// Üretilen geçici şifre ya da ondan türetilen hiçbir değer (hash dahil) buraya YAZILMAZ.
/// </summary>
public class AdminUserActionLog
{
    public long Id { get; set; }

    /// <summary>Aksiyonu yapan admin'in Keycloak <c>sub</c> claim'i.</summary>
    [MaxLength(64)]
    public string ActorKeycloakId { get; set; } = string.Empty;

    /// <summary>Yapılan aksiyon (string saklanır).</summary>
    public AdminUserAction Action { get; set; }

    /// <summary>Hedefin türü (string saklanır).</summary>
    public AdminUserTargetType TargetType { get; set; }

    /// <summary>Hedefin exam DB id'si: <see cref="TargetType"/>'a göre Teacher.Id, Student.Id ya da WorksheetComments.Id (#305).</summary>
    public int TargetId { get; set; }

    /// <summary>Sonuç (string saklanır). Yan etkili aksiyonlarda önce Requested, sonra nihai değer.</summary>
    public AdminUserActionOutcome Outcome { get; set; }

    /// <summary>Aksiyon (istek) anı (UTC).</summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// issue #277 review (security L5): <see cref="AdminUserAction.StudentSchoolChanged"/> (ve #313 <see cref="AdminUserAction.TeacherSchoolChanged"/>) için önceki okul (Schools.Id;
    /// okulsuzsa null). Diğer aksiyonlarda null. PII değil — yalnızca exam DB id'si.
    /// </summary>
    public int? FromSchoolId { get; set; }

    /// <summary>issue #277 review (security L5): <see cref="AdminUserAction.StudentSchoolChanged"/> (ve #313 <see cref="AdminUserAction.TeacherSchoolChanged"/>) için istenen/yeni okul.</summary>
    public int? ToSchoolId { get; set; }
}

/// <summary>Audit'lenen admin hesap aksiyonları. Kalıcı değer string'dir; yeniden adlandırma geçmiş kayıtları bozar.</summary>
public enum AdminUserAction
{
    PasswordReset = 1,
    /// <summary>issue #155: Keycloak hesabı devre dışı bırakıldı (+ oturumlar kapatıldı).</summary>
    AccountDisabled = 2,
    /// <summary>issue #155: Keycloak hesabı yeniden etkinleştirildi.</summary>
    AccountEnabled = 3,
    /// <summary>issue #157: öğretmen başvurusu (bağımsız öğretmen / okul bağlantısı) onaylandı.</summary>
    TeacherApproved = 4,
    /// <summary>issue #157: öğretmen başvurusu (bağımsız öğretmen / okul bağlantısı) reddedildi.</summary>
    TeacherRejected = 5,
    /// <summary>issue #277 (madde 8): admin öğrencinin okulunu değiştirdi (Students.SchoolId).</summary>
    StudentSchoolChanged = 6,
    /// <summary>issue #289: öğretmen hesap onayı askıya alındı (Teachers.AccountApprovedAt → null). Neden buraya YAZILMAZ.</summary>
    TeacherSuspended = 7,
    /// <summary>issue #289: öğretmen hesap onayının askısı kaldırıldı (Teachers.AccountApprovedAt yeniden dolu).</summary>
    TeacherUnsuspended = 8,
    /// <summary>
    /// issue #305: yorum moderasyonla gizlendi (<see cref="AdminUserTargetType.WorksheetComment"/>). Aktör admin olmayabilir
    /// (worksheet sahibi / thread'in sorumlu öğretmeni). Neden buraya YAZILMAZ; yorumun kendisinde (HiddenReason) tutulur.
    /// </summary>
    CommentHidden = 9,
    /// <summary>issue #305: gizlenen yorum yeniden görünür yapıldı.</summary>
    CommentUnhidden = 10,
    /// <summary>issue #313: admin öğretmenin okulunu ayarladı/değiştirdi (Teachers.SchoolId). Önceki/yeni okul <see cref="AdminUserActionLog.FromSchoolId"/>/<see cref="AdminUserActionLog.ToSchoolId"/>.</summary>
    TeacherSchoolChanged = 11,
    /// <summary>issue #106: öğretmen doğrudan mesajlaşmada bir öğrenciyi engelledi (<see cref="AdminUserTargetType.Conversation"/>). Aktör öğretmendir.</summary>
    DirectMessageStudentBlocked = 12,
    /// <summary>issue #106: öğretmen doğrudan mesajlaşma engelini kaldırdı.</summary>
    DirectMessageStudentUnblocked = 13
}

/// <summary>Admin hesap aksiyonunun sonucu. Kalıcı değer string'dir.</summary>
public enum AdminUserActionOutcome
{
    /// <summary>Yetki/hedef kontrolleri geçti, yan etki başlamadan yazıldı. Kalıcı olarak Requested kalan satır = yarıda kalmış aksiyon.</summary>
    Requested = 1,
    Succeeded = 2,
    /// <summary>Kendisi ya da korumalı hesap — yan etki yok.</summary>
    Denied = 3,
    /// <summary>Keycloak şifreyi set edemedi — yan etki yok.</summary>
    ResetFailed = 4,
    /// <summary>Şifre DEĞİŞTİ (ya da hesap devre dışı bırakıldı, #155) ama oturumlar kapatılamadı; şifre gösterilmedi.</summary>
    SessionRevokeFailed = 5,
    /// <summary>Öğretmen/öğrenci ya da Keycloak hesabı bulunamadı.</summary>
    NotFound = 6,
    /// <summary>issue #155: Keycloak hesap durumunu (enabled) değiştiremedi — yan etki yok.</summary>
    StatusChangeFailed = 7,
    /// <summary>issue #277 (madde 8): okuma ile koşullu yazma arasında hedef başka bir istekle değişti — yan etki yok.</summary>
    Conflict = 8,
    /// <summary>issue #277 review (security L5): okul değişikliğinde istenen okul yok — yan etki yok (<see cref="AdminUserActionLog.ToSchoolId"/> dolu).</summary>
    SchoolNotFound = 9,
    /// <summary>
    /// issue #313 review (O1): okul değişikliği UYGULANDI (DB + Keycloak ipucu) ama profil önbelleği denemelere rağmen
    /// düşürülemedi — kullanıcı en geç önbellek süresi (1 saat) boyunca eski okulun kapsamında görünebilir. Başarılı sonuçtur.
    /// </summary>
    SucceededCacheStale = 10
}

/// <summary>Admin hesap aksiyonunun hedef türü. Kalıcı değer string'dir.</summary>
public enum AdminUserTargetType
{
    Teacher = 1,
    Student = 2,
    /// <summary>issue #305: hedef bir yorum; <see cref="AdminUserActionLog.TargetId"/> = WorksheetComments.Id.</summary>
    WorksheetComment = 3,
    /// <summary>issue #106: hedef bir doğrudan mesaj konuşması; <see cref="AdminUserActionLog.TargetId"/> = Conversations.Id (öğrenci/öğretmen çifti oradan).</summary>
    Conversation = 4
}
