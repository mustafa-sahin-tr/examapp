using ExamApp.Foundation.Contracts;

namespace ExamApp.Api.Services.Interfaces;

/// <summary>Çocuk hesabı işleminin sonucu (issue #438). <see cref="ErrorCode"/> null ise başarı.</summary>
public sealed record ChildAccountResult(ChildAccountOutcome Outcome, string? ErrorCode = null, CreateChildAccountResponse? Created = null)
{
    public static readonly ChildAccountResult Ok = new(ChildAccountOutcome.Success);
}

public enum ChildAccountOutcome
{
    Success,
    /// <summary>400 — girdi (kullanıcı adı / parola / ad) geçersiz ya da Keycloak doğrulama reddi.</summary>
    Invalid,
    /// <summary>409 — kullanıcı adı alınmış (create) ya da hesap <c>parent_managed</c> değil (silme, <c>NotParentManaged</c>).</summary>
    Conflict,
    /// <summary>404 — hesap yok (şifre sıfırlamada işaretsiz hesap da 404).</summary>
    NotFound,
    /// <summary>502 — Keycloak/DB beklenmeyen hata (kompanzasyon yapıldı).</summary>
    UpstreamFailure
}

/// <summary>
/// Issue #438: velinin panelinden açılan çocuk hesabı — yalnız exam API (servis token'ı) çağırır. Keycloak kullanıcısı (e-postasız,
/// required action yok, <c>parent_managed=true</c>, Student rolü) + identity <c>User</c> satırı; Keycloak kullanıcısı oluştuktan
/// sonraki her hatada silinir (kompanzasyon). Parola hiçbir log/hata mesajına yazılmaz.
/// </summary>
public interface IChildAccountService
{
    /// <summary>
    /// İdempotent (<see cref="CreateChildAccountRequest.RequestId"/>, identity DB <c>ChildAccountRequests</c> satırı kilit noktası):
    /// Completed → mevcut hesap döner; InProgress → 409 <c>RequestInProgress</c>; Cancelled (geri alınmış/başarısız) → 409
    /// <c>RequestCancelled</c>, hesap açılmaz. Create sürerken kompanzasyon gelirse Keycloak kullanıcısı silinir, identity satırı
    /// yazılmaz. Oluşturmadan sonra <c>parent_managed</c> ve <c>child_request_id</c> yeniden okunur; saklanmamışsa hesap silinir (502).
    /// </summary>
    Task<ChildAccountResult> CreateAsync(CreateChildAccountRequest request, CancellationToken ct = default);

    /// <summary>
    /// exam API'nin kompanzasyonu: yalnız <c>parent_managed</c> işaretli hesabı (Keycloak + identity satırı) siler. Hesap yok →
    /// NotFound; var ama işaretsiz (ya da Student değil) → Conflict <c>NotParentManaged</c>.
    /// </summary>
    Task<ChildAccountResult> DeleteAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Kompanzasyon, userId bilinmiyorken: önce requestId satırı Cancelled yapılır (yoksa Cancelled eklenir — sonradan gelen create
    /// başlamaz), sonra satırdaki Keycloak id + <c>child_request_id</c> araması ile bulunan hesaplar silinir (Keycloak'ta zaten
    /// yoksa identity satırı yine temizlenir). İşaretsiz hesap varsa hiçbirine dokunulmaz (409 <c>NotParentManaged</c>).
    /// Silinecek hiçbir şey yoksa NotFound.
    /// </summary>
    Task<ChildAccountResult> DeleteByRequestIdAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>Velinin belirlediği yeni kalıcı parola + oturumları kapatma. Yalnız <c>parent_managed</c> işaretli Student hesabı.</summary>
    Task<ChildAccountResult> ResetPasswordAsync(int userId, string? password, CancellationToken ct = default);
}
