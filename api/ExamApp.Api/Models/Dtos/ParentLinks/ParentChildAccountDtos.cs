using System.ComponentModel.DataAnnotations;
using ExamApp.Foundation.Security;

namespace ExamApp.Api.Models.Dtos.ParentLinks;

/// <summary>
/// Issue #438: <c>POST api/parent/children</c> — velinin açtığı çocuk hesabı. Kullanıcı adı/parola biçimi servis katmanında
/// <see cref="ChildAccountRules"/> ile doğrulanır (alan bazlı <c>errorCode</c>); attribute'lar yalnız boyut sınırı koyar.
/// Parola yanıtta dönmez, loglanmaz.
/// </summary>
public class CreateChildAccountRequestDto
{
    [Required, MaxLength(ChildAccountRules.NameMaxLength)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(ChildAccountRules.NameMaxLength)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>4–30 karakter, <c>[a-z0-9._]</c> (kırpılıp küçük harfe çevrilir).</summary>
    [Required, MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [Required, MaxLength(ChildAccountRules.PasswordMaxLength)]
    public string Password { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int GradeId { get; set; }

    /// <summary>Opsiyonel okul; seçilirse üyelik BEKLEMEDE başlar (#361 — veli seçimi doğrulama sayılmaz).</summary>
    public int? SchoolId { get; set; }

    /// <summary>Opsiyonel öğrenci numarası (student/register ile aynı üst sınır).</summary>
    [MaxLength(50)]
    public string? StudentNumber { get; set; }

    /// <summary>Veli rızası onay kutusu — true olmalı.</summary>
    public bool ConsentAccepted { get; set; }

    /// <summary>Velinin gördüğü rıza metninin sürümü (<see cref="Services.Parents.ParentConsentVersions.Current"/>).</summary>
    [MaxLength(32)]
    public string? ConsentVersion { get; set; }
}

/// <summary>201 gövdesi — parola YOK. Çocuk bu kullanıcı adıyla giriş yapar.</summary>
public sealed record CreatedChildAccountDto(int StudentId, int LinkId, string Username, string StudentName);

/// <summary>Issue #438: <c>POST api/parent/children/{studentId}/reset-password</c> — velinin belirlediği yeni parola.</summary>
public class ResetChildPasswordRequestDto
{
    [Required, MaxLength(ChildAccountRules.PasswordMaxLength)]
    public string NewPassword { get; set; } = string.Empty;
}

/// <summary>Servis sonucu (hata eşlemesi <see cref="ParentLinkResponseDto"/> ile aynı + 502).</summary>
public class ParentChildAccountResultDto : ParentLinkResponseDto
{
    public CreatedChildAccountDto? Child { get; set; }

    /// <summary>auth-api/Keycloak erişilemedi (yarım kalan hesap geri alındı) — 502.</summary>
    public bool Upstream { get; set; }
}

/// <summary>Issue #438 hata kodları (<see cref="Foundation.Contracts.ChildAccountErrorCodes"/> ve <see cref="ParentLinkErrorCodes"/> ile birlikte).</summary>
public static class ParentChildAccountErrorCodes
{
    /// <summary>Rıza onay kutusu işaretlenmemiş — 400.</summary>
    public const string ConsentRequired = "ConsentRequired";

    /// <summary>Rıza metni sürümü güncel değil (sayfa yenilenmeli) — 400.</summary>
    public const string ConsentVersionMismatch = "ConsentVersionMismatch";

    public const string InvalidGrade = "InvalidGrade";

    public const string SchoolNotFound = "SchoolNotFound";

    /// <summary>Velinin açabileceği çocuk hesabı tavanı (10) dolu — 409.</summary>
    public const string ChildAccountLimitReached = "ChildAccountLimitReached";

    /// <summary>Çocuk hesabı veli tarafından açılmamış (kendi e-postasıyla kayıtlı) — şifresi veliden sıfırlanamaz (403).</summary>
    public const string NotParentManaged = "NotParentManaged";
}
