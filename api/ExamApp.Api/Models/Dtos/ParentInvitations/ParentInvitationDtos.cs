using System;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Models.Dtos.ParentLinks;

namespace ExamApp.Api.Models.Dtos.ParentInvitations;

/// <summary>
/// Issue #440: öğrencinin veli bilgileri — kayıt (<c>student/register</c>, <see cref="RegisterStudentDto"/> aynı alanları taşır) ve
/// "veli bilgisi ekle / e-postayı düzelt" formu (<c>POST api/student-parent/invitation</c>). Biçim kuralları:
/// <c>ExamApp.Foundation.Security.ParentContactRules</c> (e-posta, TR cep telefonu, ad).
/// </summary>
public class ParentContactRequestDto
{
    [MaxLength(200)]
    public string? ParentFullName { get; set; }

    [MaxLength(320)]
    public string? ParentEmail { get; set; }

    [MaxLength(32)]
    public string? ParentPhone { get; set; }
}

/// <summary>
/// Issue #440 / #441: öğrencinin veli durumu — profil tazeleme (<c>student.parentStatus</c>), <c>GET api/student-parent/status</c> ve
/// yazım uçlarının yanıtı. <see cref="Mode"/>: <c>Ok</c> | <c>AwaitingActivation</c> | <c>Grace</c> | <c>Expired</c>.
/// </summary>
public class ParentStatusDto
{
    public string Mode { get; set; } = string.Empty;

    /// <summary>Hesap veli kuralına tabi mi (öğrenci ve yetişkin rolü yok).</summary>
    public bool RequiresParent { get; set; }

    /// <summary>Yapılandırılmış hoşgörü süresi (gün).</summary>
    public int GraceDays { get; set; }

    /// <summary>Grace / AwaitingActivation / Expired: hoşgörü süresinin bitişi (UTC). Ok'ta null.</summary>
    public DateTime? GraceEndsAt { get; set; }

    /// <summary>Hoşgörüde kalan gün (yukarı yuvarlanmış; bittiyse 0); Ok'ta null.</summary>
    public int? DaysLeft { get; set; }

    /// <summary>Grace'in son <c>FinalWarningDays</c> günü (kapatılamaz modal) ya da Expired.</summary>
    public bool IsFinalWarning { get; set; }

    /// <summary>Açık davet — AwaitingActivation'da ve (süresi dolmuş davetle) Expired'da; yeniden gönder / düzelt için.</summary>
    public ParentInvitationSummaryDto? Invitation { get; set; }
}

/// <summary>
/// Issue #440: öğrencinin açık veli davetinin özeti. KARAR: yeni / mevcut veli ayrımı öğrenciye DÖNMEZ (e-postanın sistemde veli hesabı
/// olup olmadığı sızmasın) — UI tek metin gösterir ("Velinize davet gönderildi").
/// </summary>
public class ParentInvitationSummaryDto
{
    /// <summary>Öğrencinin yazdığı veli adı.</summary>
    public string ParentName { get; set; } = string.Empty;

    /// <summary>Maskeli e-posta (<c>a***@g***.com</c>).</summary>
    public string MaskedEmail { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Davet süresi doldu → öğrenci panelinde "Davet süresi doldu, yeniden gönder".</summary>
    public bool Expired { get; set; }

    /// <summary>Şu an yeniden gönderilebilir mi (24 saatte 1, toplam 5).</summary>
    public bool CanResend { get; set; }

    /// <summary>Bir sonraki yeniden gönderimin mümkün olduğu an (UTC); hak bittiyse null.</summary>
    public DateTime? NextResendAt { get; set; }

    public int ResendsLeft { get; set; }

    /// <summary>Doluysa yeni (farklı) bir e-posta bu ana kadar girilemez (1 saatte 3 farklı e-posta).</summary>
    public DateTime? EmailChangeLockedUntil { get; set; }

    /// <summary>YALNIZ Development ortamında: e-posta yerine ekranda gösterilen aktivasyon bağlantısı ("dev: aktivasyon bağlantısı").</summary>
    [ExamApp.Api.Services.Storage.NotStorageUrl("Uygulama içi aktivasyon sayfası bağlantısı (auth-ui /app/parent-activate); MinIO nesnesi değil")]
    public string? DevActivationUrl { get; set; }
}

/// <summary>Öğrenci veli daveti yazım uçlarının sonucu (hata bayrakları <see cref="ParentLinkResponseDto"/>'dan).</summary>
public class ParentInvitationResultDto : ParentLinkResponseDto
{
    /// <summary>Başarıda güncel durum.</summary>
    public ParentStatusDto? Status { get; set; }

    /// <summary>auth-api/Keycloak erişilemedi → 502.</summary>
    public bool Upstream { get; set; }

    /// <summary>429 yanıtlarında: yeniden denenebilecek an (UTC) — kilit bitişi ya da sonraki gönderim.</summary>
    public DateTime? RetryAt { get; set; }
}

/// <summary>Issue #440: anonim aktivasyon uçlarının gövdesi (token URL'de değil gövdede — erişim loglarına düşmesin).</summary>
public class ParentActivationTokenRequestDto
{
    [MaxLength(256)]
    public string? Token { get; set; }
}

/// <summary>Issue #440: <c>POST api/parent-activation/lookup</c> yanıtı — aktivasyon sayfasının başlığı.</summary>
public class ParentActivationLookupDto
{
    /// <summary>Öğrencinin kısa adı ("Ali K."); çözülemezse boş.</summary>
    public string StudentName { get; set; } = string.Empty;

    /// <summary>Velinin maskeli e-postası.</summary>
    public string MaskedParentEmail { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool Expired { get; set; }

    /// <summary>
    /// Token yakın zamanda (1 gün içinde) kullanılmış ya da aktivasyon sürüyor → "bu bağlantı zaten kullanıldı". Düzeltilmiş/iptal edilmiş
    /// davetin ve eski kullanılmış token'ın lookup'ı 404 <c>InvalidActivationToken</c>.
    /// </summary>
    public bool Used { get; set; }

    /// <summary>Şifre alanı gösterilsin mi (yeni hesap — <see cref="AccountExists"/> false iken).</summary>
    public bool RequiresPasswordSetup { get; set; }

    /// <summary>
    /// Security C1: bu e-postaya ait bir hesap zaten var (doğrulanmamış veli hesabı ya da davetten sonra kaydolmuş) — anonim aktivasyon
    /// yapılamaz (409 <c>LoginRequired</c>); sayfa "giriş yapıp kabul et" akışına (<c>POST api/parent-activation/accept</c>) yönlendirir.
    /// Yalnız token sahibine (= e-posta sahibine) döner. Hesap servisi erişilemezse false.
    /// </summary>
    public bool AccountExists { get; set; }

    /// <summary>Onaylanacak rıza metni sürümü (<c>ParentConsentVersions.Current</c>).</summary>
    public string ConsentVersion { get; set; } = string.Empty;

    /// <summary>Rıza metninin UI çeviri anahtarı.</summary>
    public string ConsentTextKey { get; set; } = string.Empty;

    /// <summary>Süresi dolmuş davette veli yeniden gönderim isteyebilir mi.</summary>
    public bool CanResend { get; set; }
}

/// <summary>Issue #440: <c>POST api/parent-activation/activate</c> gövdesi.</summary>
public class ParentActivationRequestDto
{
    [MaxLength(256)]
    public string? Token { get; set; }

    /// <summary>Yeni hesapta zorunlu (8-128, harf + rakam; Keycloak politikası ayrıca). Yanıtta/logda yer almaz.</summary>
    [MaxLength(128)]
    public string? Password { get; set; }

    public bool ConsentAccepted { get; set; }

    [MaxLength(32)]
    public string? ConsentVersion { get; set; }
}

/// <summary>Security C1: <c>POST api/parent-activation/accept</c> gövdesi (oturum açmış veli; token + rıza).</summary>
public class ParentInvitationAcceptRequestDto
{
    [MaxLength(256)]
    public string? Token { get; set; }

    public bool ConsentAccepted { get; set; }

    [MaxLength(32)]
    public string? ConsentVersion { get; set; }
}

/// <summary>Issue #440: aktivasyon sonucu.</summary>
public class ParentActivationResultDto
{
    public bool Activated { get; set; }

    /// <summary>false: hesap başka bir davetle zaten aktive edilmişti — parola değiştirilmedi, mevcut parolayla giriş yapılır.</summary>
    public bool PasswordSet { get; set; }
}

/// <summary>Issue #440: aktivasyon / anonim uçların servis sonucu.</summary>
public class ParentActivationOutcomeDto : ParentInvitationResultDto
{
    public ParentActivationLookupDto? Lookup { get; set; }

    public ParentActivationResultDto? Activation { get; set; }
}

/// <summary>Issue #440: mevcut velinin öğrenci davetini onaylarken verdiği rıza (<c>POST api/parent-links/{linkId}/approve</c> gövdesi).</summary>
public class ParentLinkApproveRequestDto
{
    public bool ConsentAccepted { get; set; }

    [MaxLength(32)]
    public string? ConsentVersion { get; set; }
}

/// <summary>Issue #440 hata kodları (UI dallanması).</summary>
public static class ParentInvitationErrorCodes
{
    /// <summary>400 — veli bilgisi zorunlu ama eksik (kayıt / form).</summary>
    public const string ParentInfoRequired = "ParentInfoRequired";
    public const string InvalidParentName = "InvalidParentName";
    public const string InvalidParentEmail = "InvalidParentEmail";
    public const string InvalidParentPhone = "InvalidParentPhone";

    /// <summary>409 — e-posta veli olarak kullanılamaz (öğretmen/öğrenci/admin hesabı ya da öğrencinin kendi e-postası).</summary>
    public const string ParentEmailNotUsable = "ParentEmailNotUsable";

    /// <summary>429 — 1 saatte 3 farklı e-posta denendi; <c>retryAt</c> + Retry-After.</summary>
    public const string ParentEmailChangeLocked = "ParentEmailChangeLocked";

    /// <summary>429 — aynı e-postaya 24 saatte 1 / toplam 5 yeniden gönderim sınırı; <c>retryAt</c> (null = hak bitti).</summary>
    public const string ParentInvitationResendLimited = "ParentInvitationResendLimited";

    /// <summary>409 — öğrencinin zaten Active velisi var.</summary>
    public const string ParentAlreadyLinked = "ParentAlreadyLinked";

    /// <summary>400 — hesap veli kuralına tabi değil (yetişkin rolü).</summary>
    public const string ParentNotRequired = "ParentNotRequired";

    /// <summary>404 — öğrencinin açık daveti yok (yeniden gönderim).</summary>
    public const string NoOpenInvitation = "NoOpenInvitation";

    /// <summary>502 — hesap servisi (auth-api/Keycloak) erişilemedi.</summary>
    public const string Unavailable = "ParentInvitationUnavailable";

    /// <summary>404 — aktivasyon bağlantısı geçersiz (bulunamadı / biçimsiz).</summary>
    public const string InvalidActivationToken = "InvalidActivationToken";

    /// <summary>409 — aktivasyon bağlantısının süresi doldu (yeniden gönderim istenebilir).</summary>
    public const string ActivationTokenExpired = "ActivationTokenExpired";

    /// <summary>409 — bağlantı kullanılmış ya da davet kapanmış.</summary>
    public const string ActivationTokenUsed = "ActivationTokenUsed";

    /// <summary>
    /// 409 — aktivasyon: e-posta davetten sonra veli OLMAYAN bir hesaba bağlandı (security review M2: hesap aktivasyonda açılır). Bu
    /// arada başka davetle açılmış saf veli hesabıysa hata değil — mevcut veli onayı gibi tamamlanır (<c>passwordSet:false</c>).
    /// </summary>
    public const string EmailAlreadyRegistered = "EmailAlreadyRegistered";

    /// <summary>
    /// 409 — security C1: e-postaya ait bir hesap var; anonim aktivasyon ASLA bağlantı kurmaz. Veli o hesapla giriş yapıp daveti
    /// <c>POST api/parent-activation/accept</c> ile kabul eder.
    /// </summary>
    public const string LoginRequired = "LoginRequired";

    /// <summary>403 — security C1: oturum açmış velinin hesap e-postası davet e-postası değil.</summary>
    public const string InvitationEmailMismatch = "InvitationEmailMismatch";

    /// <summary>403 — security C1: daveti yalnız saf veli hesabı kabul edebilir (öğrenci/öğretmen/admin rolü yok).</summary>
    public const string ParentAccountRequired = "ParentAccountRequired";

    public const string ConsentRequired = "ConsentRequired";
    public const string ConsentVersionMismatch = "ConsentVersionMismatch";
    public const string PasswordRejected = "PasswordRejected";

    /// <summary>409 — mevcut veli onayı: davetin süresi dolmuş (öğrenci yeniden göndermeli).</summary>
    public const string InvitationExpired = "InvitationExpired";

    /// <summary>409 — kilit/yarış; tekrar denenebilir.</summary>
    public const string Busy = "Busy";
}

/// <summary>Issue #440 / #441: yaptırım 403 kodları.</summary>
public static class ParentRequirementErrorCodes
{
    /// <summary>#440: veli daveti aktive edilmedi — booking, direkt mesaj, herkese açık yorum, liderlik tablosu kapalı.</summary>
    public const string ParentActivationRequired = "ParentActivationRequired";

    /// <summary>#441: hoşgörü süresi doldu — yalnız "veli bilgisi ekle" akışı açık.</summary>
    public const string ParentRequired = "ParentRequired";
}

/// <summary>Yaptırım 403 gövdesi: <c>{ success:false, errorCode, message }</c> (#287 <c>TeacherNotApproved</c> ile aynı şekil).</summary>
public class ParentRequirementErrorResponseDto
{
    public bool Success { get; set; }

    public string ErrorCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
