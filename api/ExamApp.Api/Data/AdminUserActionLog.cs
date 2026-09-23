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

    /// <summary>Hedefin exam DB id'si: <see cref="TargetType"/>'a göre Teacher.Id ya da Student.Id.</summary>
    public int TargetId { get; set; }

    /// <summary>Sonuç (string saklanır). Yan etkili aksiyonlarda önce Requested, sonra nihai değer.</summary>
    public AdminUserActionOutcome Outcome { get; set; }

    /// <summary>Aksiyon (istek) anı (UTC).</summary>
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>Audit'lenen admin hesap aksiyonları. Kalıcı değer string'dir; yeniden adlandırma geçmiş kayıtları bozar.</summary>
public enum AdminUserAction
{
    PasswordReset = 1
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
    /// <summary>Şifre DEĞİŞTİ ama oturumlar kapatılamadı; şifre gösterilmedi.</summary>
    SessionRevokeFailed = 5,
    /// <summary>Öğretmen/öğrenci ya da Keycloak hesabı bulunamadı.</summary>
    NotFound = 6
}

/// <summary>Admin hesap aksiyonunun hedef türü. Kalıcı değer string'dir.</summary>
public enum AdminUserTargetType
{
    Teacher = 1,
    Student = 2
}
