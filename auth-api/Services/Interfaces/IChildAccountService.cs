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
    /// <summary>409 — kullanıcı adı alınmış.</summary>
    Conflict,
    /// <summary>404 — hesap yok ya da <c>parent_managed</c> değil.</summary>
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
    Task<ChildAccountResult> CreateAsync(CreateChildAccountRequest request, CancellationToken ct = default);

    /// <summary>exam API'nin kompanzasyonu: yalnız <c>parent_managed</c> işaretli hesabı (Keycloak + identity satırı) siler; idempotent.</summary>
    Task<ChildAccountResult> DeleteAsync(int userId, CancellationToken ct = default);

    /// <summary>Velinin belirlediği yeni kalıcı parola + oturumları kapatma. Yalnız <c>parent_managed</c> işaretli Student hesabı.</summary>
    Task<ChildAccountResult> ResetPasswordAsync(int userId, string? password, CancellationToken ct = default);
}
