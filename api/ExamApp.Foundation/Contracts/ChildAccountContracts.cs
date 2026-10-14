namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #438: exam API → auth-api çocuk hesabı sözleşmesi (yalnız servis token'ı; gateway'de engelli). Üretici: exam API
/// <c>ParentChildAccountService</c>; tüketici: auth-api <c>ChildAccountsController</c>.
/// <list type="bullet">
/// <item><c>POST api/auth/child-accounts</c> → Keycloak kullanıcısı (e-postasız, required action yok, <c>parent_managed=true</c>,
/// Student rolü) + identity <c>User</c>; 201 <see cref="CreateChildAccountResponse"/>.</item>
/// <item><c>DELETE api/auth/child-accounts/{userId}</c> → kompanzasyon: yalnız <c>parent_managed</c> işaretli hesap silinir
/// (hesap yok 404 <see cref="ChildAccountErrorCodes.NotFound"/>, var ama işaretsiz 409 <see cref="ChildAccountErrorCodes.NotParentManaged"/>).</item>
/// <item><c>DELETE api/auth/child-accounts/by-request/{requestId}</c> → kompanzasyon, exam API create yanıtını alamadığında
/// (userId bilinmiyor): <see cref="CreateChildAccountRequest.RequestId"/> ile açılmış hesap silinir; aynı 404/409 ayrımı.</item>
/// <item><c>PUT api/auth/child-accounts/{userId}/password</c> → velinin belirlediği yeni parola (kalıcı) + oturumlar kapanır.</item>
/// </list>
/// Hata gövdesi: <see cref="ChildAccountErrorResponse"/>. Parola yalnız istek gövdesinde taşınır; loglanmaz, saklanmaz, dönmez.
/// </summary>
public sealed class CreateChildAccountRequest
{
    /// <summary>
    /// exam API'nin her create isteği için ürettiği kimlik (boş olamaz). Keycloak kullanıcısına
    /// <c>child_request_id</c> attribute'u olarak yazılır: aynı requestId ile tekrar gelen istek yeni hesap açmaz, mevcut hesabı
    /// döner; exam API yanıt alamazsa yarım hesabı bu kimlikle siler.
    /// </summary>
    public Guid RequestId { get; set; }

    public string Username { get; set; } = string.Empty;

    /// <summary>Yalnız istek gövdesinde; hiçbir yerde loglanmaz/saklanmaz.</summary>
    public string Password { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;
}

/// <summary>Oluşturulan hesabın identity <c>User.Id</c>'si (exam DB'de <c>Student.UserId</c>) ve Keycloak sub'ı.</summary>
public sealed class CreateChildAccountResponse
{
    public int UserId { get; set; }

    public string KeycloakId { get; set; } = string.Empty;
}

public sealed class ResetChildAccountPasswordRequest
{
    /// <summary>Yalnız istek gövdesinde; hiçbir yerde loglanmaz/saklanmaz.</summary>
    public string Password { get; set; } = string.Empty;
}

public sealed class ChildAccountErrorResponse
{
    public string ErrorCode { get; set; } = string.Empty;
}

/// <summary>Çocuk hesabı hata kodları (exam API ve auth-api ortak; UI dallanması da bunları kullanır).</summary>
public static class ChildAccountErrorCodes
{
    /// <summary>Kullanıcı adı sistemde (Keycloak) zaten var — 409.</summary>
    public const string UsernameTaken = "UsernameTaken";

    /// <summary>Kullanıcı adı biçimi geçersiz — 400.</summary>
    public const string InvalidUsername = "InvalidUsername";

    /// <summary>Parola yerel tabanı ya da Keycloak parola politikasını karşılamıyor — 400.</summary>
    public const string PasswordRejected = "PasswordRejected";

    /// <summary>Ad/soyad geçersiz — 400.</summary>
    public const string InvalidName = "InvalidName";

    /// <summary>Hesap yok — 404 (şifre sıfırlamada işaretsiz hesap da 404: varlığı sızdırılmaz).</summary>
    public const string NotFound = "NotFound";

    /// <summary>Silme: hesap var ama veli tarafından açılmamış (<c>parent_managed</c> işareti yok) — 409, dokunulmaz.</summary>
    public const string NotParentManaged = "NotParentManaged";

    /// <summary>İstek kimliği (<see cref="CreateChildAccountRequest.RequestId"/>) eksik — 400.</summary>
    public const string InvalidRequest = "InvalidRequest";

    /// <summary>Bu requestId ile bir create hâlâ sürüyor — 409 (exam API aynı requestId'yi yeniden göndermez).</summary>
    public const string RequestInProgress = "RequestInProgress";

    /// <summary>Bu requestId geri alındı (<c>DELETE by-request</c>) ya da daha önce başarısız oldu — 409; hesap açılmaz.</summary>
    public const string RequestCancelled = "RequestCancelled";

    /// <summary>Keycloak/kimlik deposu beklenmeyen hata verdi (yarım kalan hesap geri alındı) — 502.</summary>
    public const string Unavailable = "Unavailable";
}
