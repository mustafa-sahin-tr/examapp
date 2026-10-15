using ExamApp.Foundation.Contracts;

namespace ExamApp.Api.Services.Interfaces;

/// <summary>Veli hesabı işleminin sonucu (issue #440). <see cref="ErrorCode"/> null ise başarı.</summary>
public sealed record ParentAccountResult(
    ParentAccountOutcomeKind Outcome,
    string? ErrorCode = null,
    ParentAccountLookupResponse? Lookup = null,
    CreateParentAccountResponse? Created = null)
{
    public static readonly ParentAccountResult Ok = new(ParentAccountOutcomeKind.Success);
}

public enum ParentAccountOutcomeKind
{
    Success,
    /// <summary>400 — e-posta / ad / parola geçersiz.</summary>
    Invalid,
    /// <summary>409 — e-posta bu arada bir hesaba bağlandı (<c>EmailAlreadyRegistered</c>).</summary>
    Conflict,
    /// <summary>502 — Keycloak/DB beklenmeyen hata (yarım hesap geri alındı).</summary>
    UpstreamFailure
}

/// <summary>
/// Issue #440: öğrencinin davet ettiği veli — yalnız exam API (servis token'ı) çağırır. Security review M2: davet anında hesap AÇILMAZ;
/// <see cref="LookupAsync"/> salt okunurdur, hesap yalnız <see cref="CreateAsync"/> ile (veli aktivasyon bağlantısıyla parolasını
/// belirlerken) açılır. Parola hiçbir log/hata mesajına yazılmaz.
/// </summary>
public interface IParentAccountService
{
    /// <summary>
    /// Salt okunur. Keycloak'ta bu e-postayla tek kullanıcı varsa: saf veli (yalnız Parent rolü) + identity satırı Parent + e-posta DOĞRULANMIŞ
    /// ise <c>ExistingParent</c>; saf veli ama e-postası doğrulanmamış ya da identity satırı yoksa <c>UnverifiedParent</c> (security C1: token'lı
    /// davet, kabul oturum + token ile); veli olmayan hesap <c>NotParentAccount</c>. Keycloak'ta yoksa: identity'de bu e-postayla (silinmiş
    /// dahil) satır varsa <c>NotParentAccount</c>, yoksa <c>None</c>.
    /// </summary>
    Task<ParentAccountResult> LookupAsync(string? email, CancellationToken ct = default);

    /// <summary>
    /// Aktivasyon: e-posta hâlâ hiçbir hesaba ait değilse Keycloak kullanıcısı (kullanıcı adı = e-posta, Parent, <c>emailVerified=true</c>,
    /// kalıcı parola) + identity satırı. E-posta bu arada kaydedildiyse 409 <c>EmailAlreadyRegistered</c>. Keycloak kullanıcısı açıldıktan
    /// sonraki her hatada (parola politikası dahil) kullanıcı silinir.
    /// </summary>
    Task<ParentAccountResult> CreateAsync(CreateParentAccountRequest request, CancellationToken ct = default);

    /// <summary>
    /// Security C1 (oturum açmış velinin davet kabulü): identity <c>userId</c> saf veli hesabı ve Keycloak'taki e-postası davet e-postasına
    /// eşitse <c>emailVerified=true</c>. Eşit değil → 409 <c>EmailMismatch</c>; saf veli değil / yok → 409 <c>NotParentAccount</c>.
    /// </summary>
    Task<ParentAccountResult> ConfirmEmailAsync(ConfirmParentEmailRequest request, CancellationToken ct = default);
}
