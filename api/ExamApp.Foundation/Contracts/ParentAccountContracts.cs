namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #440: exam API → auth-api veli hesabı sözleşmesi (yalnız servis token'ı — <c>Service</c> policy; gateway'de engelli,
/// <c>child-accounts</c> deseni). Üretici: exam API <c>ParentInvitationService</c>; tüketici: auth-api <c>ParentAccountsController</c>.
/// Security review M2: davet anında HİÇBİR hesap açılmaz — Keycloak kullanıcısı yalnız veli aktivasyon bağlantısıyla parolasını
/// belirlerken açılır (bekleyen/şifresiz hesap, "şifremi unuttum" ile aktivasyonu atlama ve silme yarışı yok).
/// <list type="bullet">
/// <item><c>POST api/auth/parent-accounts/lookup</c> → salt okunur: e-posta DOĞRULANMIŞ saf veli hesabına aitse
/// <see cref="ParentAccountOutcomes.ExistingParent"/> (+ userId/keycloakId), doğrulanmamış saf veli hesabına aitse
/// <see cref="ParentAccountOutcomes.UnverifiedParent"/> (exam API bunu <see cref="ParentAccountOutcomes.None"/> gibi işler: token'lı davet),
/// hiçbir hesaba ait değilse <see cref="ParentAccountOutcomes.None"/>, veli olmayan (öğrenci/öğretmen/admin/rolsüz) ya da sahibi belirsiz bir
/// hesaba aitse <see cref="ParentAccountOutcomes.NotParentAccount"/>.</item>
/// <item><c>POST api/auth/parent-accounts/activate</c> → yeni veli hesabı: kullanıcı adı = e-posta, Parent rolü, <c>emailVerified=true</c>,
/// kalıcı parola + identity <c>User</c>. E-posta bu arada kaydedildiyse 409 <see cref="ParentAccountErrorCodes.EmailAlreadyRegistered"/>
/// (hiçbir hesaba dokunulmaz).</item>
/// <item><c>POST api/auth/parent-accounts/confirm-email</c> → security C1: oturum açmış velinin hesabı (identity userId → Keycloak) saf veli ve
/// SUNUCUDAKİ e-postası davet e-postasına eşitse <c>emailVerified=true</c> yapılır (204); eşit değilse 409
/// <see cref="ParentAccountErrorCodes.EmailMismatch"/>, saf veli değilse 409 <see cref="ParentAccountErrorCodes.NotParentAccount"/>.</item>
/// </list>
/// Parola yalnız istek gövdesinde taşınır; loglanmaz, saklanmaz, dönmez.
/// </summary>
public sealed class ParentAccountLookupRequest
{
    /// <summary>Normalize edilmiş (küçük harf, kırpılmış) e-posta.</summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>Lookup sonucu: <see cref="Outcome"/> + (yalnız ExistingParent'ta) identity <c>User.Id</c> ve Keycloak sub'ı.</summary>
public sealed class ParentAccountLookupResponse
{
    /// <summary><see cref="ParentAccountOutcomes"/> değerlerinden biri.</summary>
    public string Outcome { get; set; } = string.Empty;

    public int? UserId { get; set; }

    public string? KeycloakId { get; set; }
}

/// <summary>Security C1: davet token'ını kabul eden oturum açmış velinin hesabı + davet e-postası.</summary>
public sealed class ConfirmParentEmailRequest
{
    /// <summary>Velinin identity <c>User.Id</c>'si (exam API'de oturumdan çözülür).</summary>
    public int UserId { get; set; }

    /// <summary>Davetin normalize e-postası.</summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>Aktivasyonda yeni veli hesabı (şifre yalnız gövdede).</summary>
public sealed class CreateParentAccountRequest
{
    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>Yalnız istek gövdesinde; hiçbir yerde loglanmaz/saklanmaz.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Açılan veli hesabının identity <c>User.Id</c>'si (exam DB'de <c>Parent.UserId</c>) ve Keycloak sub'ı.</summary>
public sealed class CreateParentAccountResponse
{
    public int UserId { get; set; }

    public string KeycloakId { get; set; } = string.Empty;
}

public sealed class ParentAccountErrorResponse
{
    public string ErrorCode { get; set; } = string.Empty;
}

/// <summary><see cref="ParentAccountLookupResponse.Outcome"/> değerleri.</summary>
public static class ParentAccountOutcomes
{
    /// <summary>E-posta DOĞRULANMIŞ, aktif bir veli hesabına ait (rol yalnız Parent; identity satırı var).</summary>
    public const string ExistingParent = "ExistingParent";

    /// <summary>
    /// Security C1: e-posta saf veli hesabına ait ama e-postası DOĞRULANMAMIŞ (realm verifyEmail kapalı; veli e-postasını kendisi
    /// değiştirebilir). Uygulama içi onaya güvenilmez — exam API token'lı davet gönderir; kabul yalnız oturum + token ile.
    /// </summary>
    public const string UnverifiedParent = "UnverifiedParent";

    /// <summary>E-posta hiçbir hesaba ait değil — veli aktivasyonda hesabını açar.</summary>
    public const string None = "None";

    /// <summary>E-posta veli OLMAYAN ya da sahibi belirsiz bir hesaba ait — davet için kullanılamaz.</summary>
    public const string NotParentAccount = "NotParentAccount";
}

/// <summary>Veli hesabı hata kodları (exam API ve auth-api ortak).</summary>
public static class ParentAccountErrorCodes
{
    /// <summary>E-posta biçimi geçersiz — 400.</summary>
    public const string InvalidEmail = "InvalidEmail";

    /// <summary>Ad geçersiz — 400.</summary>
    public const string InvalidName = "InvalidName";

    /// <summary>Parola yerel tabanı ya da Keycloak parola politikasını karşılamıyor — 400.</summary>
    public const string PasswordRejected = "PasswordRejected";

    /// <summary>Aktivasyon: e-posta bu arada (başka davetle / kayıtla) bir hesaba bağlandı — 409; hiçbir hesaba dokunulmaz.</summary>
    public const string EmailAlreadyRegistered = "EmailAlreadyRegistered";

    /// <summary>confirm-email: hesabın e-postası davet e-postası değil — 409.</summary>
    public const string EmailMismatch = "EmailMismatch";

    /// <summary>confirm-email: hesap saf veli değil / yok — 409.</summary>
    public const string NotParentAccount = "NotParentAccount";

    /// <summary>Keycloak/kimlik deposu beklenmeyen hata verdi — 502.</summary>
    public const string Unavailable = "Unavailable";
}
