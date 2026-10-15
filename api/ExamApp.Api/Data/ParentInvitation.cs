using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Issue #440: veli davetinin durumu. String saklanır.</summary>
public enum ParentInvitationStatus
{
    /// <summary>Açık davet: veli henüz aktive etmedi / onaylamadı (süresi dolmuş olabilir — <see cref="ParentInvitation.ExpiresAt"/>).</summary>
    Pending = 0,
    /// <summary>Veli aktive etti (yeni hesap) ya da onayladı (mevcut veli); bağlantı Active.</summary>
    Activated = 1,
    /// <summary>Öğrenci veli e-postasını düzeltti; yerine yeni davet açıldı (eski token geçersiz).</summary>
    Superseded = 2,
    /// <summary>Veli reddetti ya da bağlantı başka yoldan kapandı.</summary>
    Cancelled = 3
}

/// <summary>
/// Issue #440 (epic #435): öğrencinin kayıtta / "veli bilgisi ekle" formunda verdiği veliye gönderilen davet. Mevcut veli hesabına
/// gidiyorsa davetle birlikte <see cref="ParentStudentLink"/> (<see cref="ParentStudentLinkOrigin.StudentRegistered"/>, Pending) açılır,
/// veli girişte rızayla onaylar. E-posta hiçbir hesaba ait değilse (security review M2) davet anında HİÇBİR hesap/veli satırı/bağlantı
/// açılmaz: veli tek kullanımlık token'la parolasını belirlerken hesap + Parent satırı + Active bağlantı birlikte oluşur. Düz token
/// yalnız e-postadaki bağlantıda; burada yalnız SHA-256 hash'i (<see cref="TokenHash"/>). Token öğrenci + e-posta çiftine bağlıdır: e-posta
/// düzeltilirse davet <see cref="ParentInvitationStatus.Superseded"/> olur. Öğrenci başına en fazla bir Pending davet (filtreli tekil
/// index). <see cref="BaseEntity"/>'den türemez: durum makinesi + append-only geçmiş.
/// </summary>
public class ParentInvitation
{
    [Key]
    public Guid Id { get; set; }

    public int StudentId { get; set; }

    public Student Student { get; set; } = null!;

    /// <summary>Velinin identity <c>User.Id</c>'si — mevcut veli davetinde baştan, yeni velide aktivasyonla dolar.</summary>
    public int? ParentUserId { get; set; }

    /// <summary>Velinin Keycloak sub'ı (bildirim hedefi) — yalnız hesap varken.</summary>
    [MaxLength(50)]
    public string? ParentKeycloakId { get; set; }

    public int? ParentId { get; set; }

    public Parent? Parent { get; set; }

    /// <summary>StudentRegistered bağlantısı — mevcut velide davetle (Pending), yeni velide aktivasyonla (Active) açılır.</summary>
    public int? LinkId { get; set; }

    public ParentStudentLink? Link { get; set; }

    /// <summary>Normalize edilmiş (küçük harf) veli e-postası.</summary>
    [Required, MaxLength(254)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Öğrencinin yazdığı veli adı.</summary>
    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>E.164 TR cep telefonu (<c>+905xxxxxxxxx</c>) — yalnız saklanır (SMS kapsam dışı).</summary>
    [Required, MaxLength(16)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>Aktivasyon token'ının SHA-256 hash'i (hex). Mevcut veli davetinde (token yok) null.</summary>
    [MaxLength(64)]
    public string? TokenHash { get; set; }

    /// <summary>Davetin (token'ın) geçerlilik sonu (UTC): son gönderim + 7 gün.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Token kullanıldığı an (UTC); kullanılmadıysa null. Tek kullanım koşullu UPDATE ile işaretlenir.</summary>
    public DateTime? UsedAt { get; set; }

    public ParentInvitationStatus Status { get; set; }

    /// <summary>true: e-posta hiçbir hesaba ait değildi — veli hesabını token'la aktivasyonda açar; false: mevcut veli girişte onaylar.</summary>
    public bool IsNewAccount { get; set; }

    /// <summary>Bu davetle yapılan gönderim sayısı (ilk gönderim dahil).</summary>
    public int SendCount { get; set; }

    /// <summary>Son gönderim anı (UTC).</summary>
    public DateTime LastSentAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Durumun son değiştiği an (UTC): aktivasyon / düzeltme / iptal.</summary>
    public DateTime? ClosedAt { get; set; }
}

/// <summary>
/// Issue #440: öğrencinin veli e-postası denemesi (kayıt ya da "veli bilgisi ekle"; kullanılamayan e-posta dahil). "1 saatte 3 farklı
/// e-posta → 1 saat kilit" kuralı buradan hesaplanır. Öğrenci satırı olmadan da yazılabilsin diye öğrencinin kullanıcı id'siyle
/// tutulur. E-posta düz değil, SHA-256 hash'i saklanır (yalnız farklılık sayılır). FK yok, append-only.
/// </summary>
public class ParentInvitationAttempt
{
    public long Id { get; set; }

    /// <summary>Öğrencinin identity <c>User.Id</c>'si (<see cref="Student.UserId"/>).</summary>
    public int StudentUserId { get; set; }

    /// <summary>Normalize edilmiş e-postanın SHA-256 hash'i (hex).</summary>
    [Required, MaxLength(64)]
    public string EmailHash { get; set; } = string.Empty;

    public DateTime AttemptedAt { get; set; }
}

/// <summary>
/// Issue #441: "veli ekleme süresi bitiyor" hatırlatmasının işlendiği işaretçi — (öğrenci, bitiş anı, kalan gün) başına en fazla bir
/// <c>StudentParentGraceReminderEvent</c>. Event'le aynı transaction'da yazılır; tekil index job tekrarı / yarışında tekilleştirir.
/// Süre yeniden başlarsa (bağlantı koparıldı) bitiş anı değişir → yeni hatırlatmalar. FK yok.
/// </summary>
public class StudentParentGraceReminder
{
    public long Id { get; set; }

    public int StudentId { get; set; }

    public DateTime GraceEndsAt { get; set; }

    /// <summary>7 ya da 1.</summary>
    public int DaysLeft { get; set; }

    public DateTime SentAt { get; set; }
}
