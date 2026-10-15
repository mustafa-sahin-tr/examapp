using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentInvitations;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Parents;

/// <summary>Davet işlemini başlatan öğrenci (token'dan çözülmüş kimlik).</summary>
public sealed record ParentInvitationStudent(int UserId, string KeycloakId, string? FullName);

/// <summary>Doğrulanmış + normalize edilmiş veli iletişim bilgisi ya da ilk hata kodu.</summary>
public sealed record ParentContactValidation(string? ErrorCode, string FullName = "", string Email = "", string Phone = "")
{
    public bool IsValid => ErrorCode == null;
}

/// <summary>
/// Issue #440 review (code M1): öğrenci satırından bağımsız tüm ön kontrolleri (biçim, kilit, gönderim/hedef e-posta tavanları, auth-api
/// salt okunur araması) geçmiş davet. <see cref="IParentInvitationService.CommitAsync"/> bunu yazar.
/// </summary>
public sealed record PreparedParentInvitation(
    ParentContactValidation Contact,
    bool StudentExisted,
    Guid? ResendOfInvitationId,
    int? ExistingParentUserId,
    string? ExistingParentKeycloakId);

/// <summary>Ön kontrol sonucu: hata ya da hazır davet.</summary>
public sealed record ParentInvitationPrecheck(ParentInvitationResultDto? Failure, PreparedParentInvitation? Prepared)
{
    public bool Success => Failure == null;
}

/// <summary>
/// Issue #440 (epic #435, veli-öncelikli (b)): öğrencinin kayıtta / "veli bilgisi ekle" formunda verdiği veliye davet; veli tarafında
/// aktivasyon (yeni hesap — hesap YALNIZ burada açılır, security review M2) ya da onay (mevcut veli). Yaptırım durumu
/// <see cref="IStudentParentStatusService"/>'ten okunur.
/// </summary>
public interface IParentInvitationService
{
    /// <summary>Kayıtta veli alanları zorunlu mu: veli kuralına tabi + Active velisi yok + açık daveti yok (öğrenci satırı yoksa da true).</summary>
    Task<bool> NeedsParentInfoAsync(int studentUserId, bool requiresParent, CancellationToken ct = default);

    /// <summary>
    /// Öğrenci satırına bağlı OLMAYAN tüm kontroller (kayıtta öğrenci satırı yazılmadan ÖNCE çalışır): biçim → Active veli → 1 saatte 3 farklı
    /// e-posta kilidi → aynı öğrenci+e-postaya gönderim sınırı → hedef e-postaya 24 saatte en fazla 3 davet → deneme kaydı → auth-api salt
    /// okunur araması (veli olmayan hesap 409 <c>ParentEmailNotUsable</c>). Aynı e-postayla açık davet varsa yeniden gönderim olarak işaretlenir.
    /// </summary>
    Task<ParentInvitationPrecheck> PrecheckAsync(ParentInvitationStudent student, ParentContactRequestDto? request, bool requiresParent, CancellationToken ct = default);

    /// <summary>
    /// Ön kontrolden geçmiş daveti yazar (öğrenci satırı mevcut olmalı): TEK transaction'da (öğrenci kilidi) eski açık davet Superseded
    /// (bağlantısı Revoked), mevcut velide Parent satırı + StudentRegistered/Pending bağlantı, davet, hoşgörü damgası (boşsa) ve
    /// <c>ParentInvitationRequestedEvent</c>. Öğrenciye mevcut/yeni veli ayrımı söylenmez.
    /// </summary>
    Task<ParentInvitationResultDto> CommitAsync(ParentInvitationStudent student, PreparedParentInvitation prepared, CancellationToken ct = default);

    /// <summary><see cref="PrecheckAsync"/> + <see cref="CommitAsync"/> ("veli bilgisi ekle" formu).</summary>
    Task<ParentInvitationResultDto> StartAsync(ParentInvitationStudent student, ParentContactRequestDto? request, bool requiresParent, CancellationToken ct = default);

    /// <summary>
    /// Kayıt geri alma (code M1): bu istekte açılmış, henüz hiçbir bağlantı/davet taşımayan öğrenci satırını gerçekten siler (davet yazımı
    /// kayıttan sonra başarısız olduysa — kullanıcı öğrenci kaydı yokmuş gibi devam eder, ör. öğretmen olarak kaydolabilir).
    /// </summary>
    Task DiscardNewStudentAsync(int studentUserId, CancellationToken ct = default);

    /// <summary>Öğrenci: açık daveti yeniden gönderir (aynı e-postaya 24 saatte 1, toplam 5; yeni token, süre yeniden 7 gün).</summary>
    Task<ParentInvitationResultDto> ResendAsync(ParentInvitationStudent student, bool requiresParent, CancellationToken ct = default);

    /// <summary>Öğrencinin durumu; <paramref name="stamp"/> = profil tazeleme (ilk girişte hoşgörü sayacını başlatır).</summary>
    Task<ParentStatusDto> GetStatusAsync(int studentUserId, bool requiresParent, bool stamp, CancellationToken ct = default);

    /// <summary>
    /// Anonim: token'ın gösterdiği davetin özeti (aktivasyon sayfası). Bilinmeyen, düzeltilmiş/iptal edilmiş ya da 1 günden eski kullanılmış
    /// token 404 <c>InvalidActivationToken</c>; yeni kullanılmış token <c>used:true</c>.
    /// </summary>
    Task<ParentActivationOutcomeDto> LookupAsync(string? token, CancellationToken ct = default);

    /// <summary>
    /// Anonim: token + parola + rıza → auth-api'de veli hesabı AÇILIR (kalıcı parola), Parent satırı, ParentConsent, Active (+birincil)
    /// bağlantı, davet Activated, ParentLinkedEvent. Tek kullanım koşullu UPDATE ile (token hash'i dahil). Security C1: e-postaya ait HERHANGİ
    /// bir hesap varsa anonim yol asla bağlantı kurmaz → 409 <c>LoginRequired</c> (veli giriş yapıp <see cref="AcceptAsync"/> ile kabul eder).
    /// </summary>
    Task<ParentActivationOutcomeDto> ActivateAsync(ParentActivationRequestDto? request, string? ipAddress, CancellationToken ct = default);

    /// <summary>Anonim: veli, süresi dolmuş (ya da gelmemiş) bağlantıyı yeniden ister — öğrenci yeniden gönderimiyle aynı sınırlar.</summary>
    Task<ParentActivationOutcomeDto> ResendByTokenAsync(string? token, CancellationToken ct = default);

    /// <summary>
    /// Security C1: OTURUM AÇMIŞ velinin token'lı daveti kabulü (e-postaya ait hesap zaten varken tek yol). Token geçerli/açık olmalı;
    /// auth-api çağıranın hesabının saf veli olduğunu ve SUNUCUDAKİ e-postasının davet e-postası olduğunu doğrular (sonra
    /// <c>emailVerified=true</c>) — token e-posta sahipliğini, oturum hesap sahipliğini kanıtlar. Ardından rıza + Active bağlantı (+birincil),
    /// token kullanıldı. E-posta eşleşmezse 403 <c>InvitationEmailMismatch</c>, saf veli değilse 403 <c>ParentAccountRequired</c>.
    /// </summary>
    Task<ParentActivationOutcomeDto> AcceptAsync(ParentInvitationAcceptRequestDto? request, int callerUserId, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Mevcut veli: kendi StudentRegistered/Pending bağlantısını rızayla onaylar. Bağlantı çağıranın StudentRegistered bağlantısı değilse
    /// null (çağıran eski onay yoluna düşer).
    /// </summary>
    Task<ParentLinkResponseDto?> ApproveAsInvitedParentAsync(int linkId, int parentUserId, ParentLinkApproveRequestDto? consent, string? ipAddress, CancellationToken ct = default);

    /// <summary>Mevcut veli: daveti reddeder (bağlantı Revoked, davet Cancelled). Uygulanamazsa null.</summary>
    Task<ParentLinkResponseDto?> RejectAsInvitedParentAsync(int linkId, int parentUserId, CancellationToken ct = default);
}

/// <inheritdoc cref="IParentInvitationService"/>
public sealed class ParentInvitationService : IParentInvitationService
{
    public const string KindNewParentActivation = "NewParentActivation";
    public const string KindExistingParentApproval = "ExistingParentApproval";
    public const string ConsentTextKey = "parentConsent.kvkk";

    private static readonly HashSet<string> ParentLinkCodes = new(StringComparer.Ordinal)
    {
        ParentLinkErrorCodes.ProfileNotFound, ParentLinkErrorCodes.NotFound, ParentLinkErrorCodes.ParentLimitReached,
        ParentLinkErrorCodes.StudentLimitReached
    };

    private readonly AppDbContext _context;
    private readonly IStudentParentStatusService _status;
    private readonly IParentAccountDirectory _directory;
    private readonly IParentActivationDevLinkStore _devLinks;
    private readonly ActivationLinkProtector _linkProtector;
    private readonly IOptionsMonitor<ParentRequirementOptions> _options;
    private readonly IConfiguration? _configuration;
    private readonly IAuthApiClient? _authApi;
    private readonly TimeProvider _time;
    private readonly ILogger<ParentInvitationService> _logger;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentInvitationService(
        AppDbContext context,
        IStudentParentStatusService status,
        IParentAccountDirectory directory,
        IParentActivationDevLinkStore devLinks,
        ActivationLinkProtector linkProtector,
        IOptionsMonitor<ParentRequirementOptions> options,
        IConfiguration? configuration = null,
        IAuthApiClient? authApi = null,
        TimeProvider? time = null,
        ILogger<ParentInvitationService>? logger = null,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _status = status;
        _directory = directory;
        _devLinks = devLinks;
        _linkProtector = linkProtector;
        _options = options;
        _configuration = configuration;
        _authApi = authApi;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ParentInvitationService>.Instance;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ---- doğrulama ----------------------------------------------------------------------------------------------------

    /// <summary>Alan sırası UI'nin hata gösterme sırasıdır: ad → e-posta → telefon. Hepsi boşsa <c>ParentInfoRequired</c>.</summary>
    public static ParentContactValidation ValidateContact(string? fullName, string? email, string? phone)
    {
        if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
            return new ParentContactValidation(ParentInvitationErrorCodes.ParentInfoRequired);
        var name = ParentContactRules.NormalizeName(fullName);
        if (name == null)
            return new ParentContactValidation(ParentInvitationErrorCodes.InvalidParentName);
        var normalizedEmail = ParentContactRules.NormalizeEmail(email);
        if (normalizedEmail == null)
            return new ParentContactValidation(ParentInvitationErrorCodes.InvalidParentEmail);
        var normalizedPhone = ParentContactRules.NormalizePhone(phone);
        if (normalizedPhone == null)
            return new ParentContactValidation(ParentInvitationErrorCodes.InvalidParentPhone);
        return new ParentContactValidation(null, name, normalizedEmail, normalizedPhone);
    }

    public async Task<bool> NeedsParentInfoAsync(int studentUserId, bool requiresParent, CancellationToken ct = default)
    {
        if (!requiresParent)
            return false;
        var facts = await StudentParentStatusService.LoadFactsAsync(_context, studentUserId, Now, ct);
        return facts == null || (!facts.HasActiveParent && facts.Invitation == null);
    }

    // ---- öğrenci: ön kontrol / yazım / yeniden gönderim -------------------------------------------------------------

    public async Task<ParentInvitationPrecheck> PrecheckAsync(
        ParentInvitationStudent student, ParentContactRequestDto? request, bool requiresParent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(student);
        if (!requiresParent)
            return Reject(Fail(ParentInvitationErrorCodes.ParentNotRequired));

        var contact = ValidateContact(request?.ParentFullName, request?.ParentEmail, request?.ParentPhone);
        if (!contact.IsValid)
            return Reject(Fail(contact.ErrorCode!));

        var now = Now;
        var facts = await StudentParentStatusService.LoadFactsAsync(_context, student.UserId, now, ct);
        if (facts?.HasActiveParent == true)
            return Reject(Fail(ParentInvitationErrorCodes.ParentAlreadyLinked, conflict: true));

        // Aynı e-posta: düzeltme değil yeniden gönderim (sınırları ResendCore uygular; ad/telefon da güncellenir).
        if (facts?.Invitation != null && string.Equals(facts.Invitation.Email, contact.Email, StringComparison.Ordinal))
            return new ParentInvitationPrecheck(null, new PreparedParentInvitation(contact, true, facts.Invitation.Id, null, null));

        var emailHash = ParentInvitationLimits.EmailHash(contact.Email);
        var attempts = facts?.RecentAttempts ?? await StudentParentStatusService.RecentAttemptsAsync(_context, student.UserId, now, ct);
        if (ParentInvitationLimits.EmailChangeLockedUntil(attempts, emailHash, now) is DateTime lockedUntil)
            return Reject(RateLimited(ParentInvitationErrorCodes.ParentEmailChangeLocked, lockedUntil, now));

        if (facts != null)
        {
            var history = await StudentParentStatusService.SendHistoryAsync(_context, facts.StudentId, contact.Email, ct);
            var decision = ParentInvitationLimits.Resend(history.Total, history.LastSent, now);
            if (!decision.Allowed)
                return Reject(RateLimited(ParentInvitationErrorCodes.ParentInvitationResendLimited, decision.NextResendAt, now));
        }

        // Anti-spam: hedef e-postaya tüm öğrencilerden 24 saatte en fazla N yeni davet (aynı genel kod — kimin davet ettiği sızmaz).
        var windowStart = now - ParentInvitationRules.TargetEmailWindow;
        var recentToEmail = await _context.ParentInvitations.AsNoTracking()
            .Where(i => i.Email == contact.Email && i.CreatedAt > windowStart)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => i.CreatedAt)
            .Take(ParentInvitationRules.MaxInvitationsPerTargetEmail)
            .ToListAsync(ct);
        if (recentToEmail.Count >= ParentInvitationRules.MaxInvitationsPerTargetEmail)
        {
            var retryAt = DateTime.SpecifyKind(recentToEmail.Min() + ParentInvitationRules.TargetEmailWindow, DateTimeKind.Utc);
            return Reject(RateLimited(ParentInvitationErrorCodes.ParentInvitationResendLimited, retryAt, now));
        }

        // Deneme (kullanılamayan e-posta dahil) kilit sayımına girer — auth-api'ye gitmeden ÖNCE yazılır.
        _context.ParentInvitationAttempts.Add(new ParentInvitationAttempt { StudentUserId = student.UserId, EmailHash = emailHash, AttemptedAt = now });
        await _context.SaveChangesAsync(CancellationToken.None);
        _context.ChangeTracker.Clear();
        _status.Invalidate(student.UserId);

        var lookup = await _directory.LookupAsync(contact.Email, ct);
        switch (lookup.Status)
        {
            case ParentAccountCallStatus.Success:
                break;
            case ParentAccountCallStatus.Invalid:
                return Reject(Fail(ParentInvitationErrorCodes.InvalidParentEmail));
            default:
                return Reject(Upstream());
        }

        var found = lookup.Lookup!;
        if (found.Outcome == ParentAccountOutcomes.NotParentAccount
            || (found.Outcome == ParentAccountOutcomes.ExistingParent && found.UserId == student.UserId))
        {
            _logger.LogInformation("[ParentInvitations] Veli e-postası kullanılamaz (veli olmayan hesap): studentUserId={StudentUserId}", student.UserId);
            return Reject(Fail(ParentInvitationErrorCodes.ParentEmailNotUsable, conflict: true));
        }

        // Security C1: yalnız DOĞRULANMIŞ e-postalı veli hesabı uygulama içi onaya gider. Doğrulanmamış veli hesabı (UnverifiedParent) None
        // gibi token'lı davet alır — token e-posta sahipliğini kanıtlar; o hesap ancak giriş yapıp token'la kabul edebilir (AcceptAsync).
        return found.Outcome == ParentAccountOutcomes.ExistingParent
            ? new ParentInvitationPrecheck(null, new PreparedParentInvitation(contact, facts != null, null, found.UserId, found.KeycloakId))
            : new ParentInvitationPrecheck(null, new PreparedParentInvitation(contact, facts != null, null, null, null));
    }

    public async Task<ParentInvitationResultDto> StartAsync(
        ParentInvitationStudent student, ParentContactRequestDto? request, bool requiresParent, CancellationToken ct = default)
    {
        var precheck = await PrecheckAsync(student, request, requiresParent, ct);
        return precheck.Success ? await CommitAsync(student, precheck.Prepared!, ct) : precheck.Failure!;
    }

    public async Task<ParentInvitationResultDto> CommitAsync(
        ParentInvitationStudent student, PreparedParentInvitation prepared, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(student);
        ArgumentNullException.ThrowIfNull(prepared);
        if (prepared.ResendOfInvitationId is Guid resendOf)
            return await ResendCoreAsync(resendOf, student, prepared.Contact, ct);

        var contact = prepared.Contact;
        var studentId = await _context.Students.AsNoTracking().Where(s => s.UserId == student.UserId)
            .OrderBy(s => s.Id).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        if (studentId == null)
            return Fail(ParentLinkErrorCodes.ProfileNotFound, notFound: true);

        var isNewAccount = prepared.ExistingParentUserId == null;
        string? error = null;
        ParentInvitation? invitation = null;
        string? activationUrl = null;
        var superseded = false;
        try
        {
            await ParentLinkPrimary.InStudentLockAsync(_context, studentId.Value, async token =>
            {
                error = null;
                invitation = null;
                activationUrl = null;
                superseded = false;
                var now = Now;

                if (await _context.ParentStudentLinks.Where(l => l.StudentId == studentId.Value).Where(ParentChildAccess.GrantsAccess).AnyAsync(token))
                {
                    error = ParentInvitationErrorCodes.ParentAlreadyLinked;
                    return false;
                }

                // Açık davet (başka e-posta) → düzeltme: eski davet Superseded, bağlantısı Revoked; eski token geçersiz.
                var open = await _context.ParentInvitations.Where(StudentParentStatusService.OpenInvitationOf(studentId.Value))
                    .Select(i => new { i.Id, i.LinkId, i.Email, i.UsedAt })
                    .FirstOrDefaultAsync(token);
                if (open != null)
                {
                    // Aynı e-posta (eşzamanlı istek) ya da aktivasyon sürerken (token talep edilmiş — code MINOR3) düzeltme yapılmaz.
                    if (string.Equals(open.Email, contact.Email, StringComparison.Ordinal)
                        || (open.UsedAt is DateTime claimedAt && DateTime.SpecifyKind(claimedAt, DateTimeKind.Utc) > now - ParentInvitationRules.ClaimStaleAfter))
                    {
                        error = ParentInvitationErrorCodes.Busy;
                        return false;
                    }
                    await CloseInvitationAsync(open.Id, open.LinkId, ParentInvitationStatus.Superseded, student.UserId, now, token);
                    superseded = true;
                }
                // Bağlantısı başka yoldan kapanmış bayat Pending davetler de kapatılır (öğrenci başına tek Pending — tekil index).
                await _context.ParentInvitations
                    .Where(i => i.StudentId == studentId.Value && i.Status == ParentInvitationStatus.Pending)
                    .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, ParentInvitationStatus.Cancelled)
                        .SetProperty(i => i.ClosedAt, now), token);

                int? parentId = null;
                int? linkId = null;
                if (!isNewAccount)
                {
                    // Mevcut veli: Parent satırı + rızalı onay bekleyen bağlantı. Yeni velide (M2) hiçbir şey açılmaz — aktivasyonda.
                    parentId = await EnsureParentRowAsync(prepared.ExistingParentUserId!.Value, token);
                    await _context.ParentStudentLinks
                        .Where(l => l.ParentId == parentId && l.StudentId == studentId.Value && l.Status == ParentStudentLinkStatus.Pending)
                        .ExecuteUpdateAsync(set => set.SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                            .SetProperty(l => l.RevokedAt, now), token);
                    var link = new ParentStudentLink
                    {
                        ParentId = parentId.Value,
                        StudentId = studentId.Value,
                        Status = ParentStudentLinkStatus.Pending,
                        Origin = ParentStudentLinkOrigin.StudentRegistered,
                        IsPrimary = false,
                        CreatedAt = now
                    };
                    _context.ParentStudentLinks.Add(link);
                    await _context.SaveChangesAsync(token);
                    linkId = link.Id;
                }

                // Review M3: davet beklerken de hoşgörü süresi işler (yeni kayıt dahil) — damga yoksa şimdi.
                await _context.Students.Where(s => s.Id == studentId.Value && s.ParentRequirementStartedAt == null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.ParentRequirementStartedAt, now), token);

                var rawToken = isNewAccount ? ParentActivationTokens.Generate() : null;
                invitation = new ParentInvitation
                {
                    Id = Guid.NewGuid(),
                    StudentId = studentId.Value,
                    ParentUserId = prepared.ExistingParentUserId,
                    ParentKeycloakId = prepared.ExistingParentKeycloakId,
                    ParentId = parentId,
                    LinkId = linkId,
                    Email = contact.Email,
                    FullName = contact.FullName,
                    Phone = contact.Phone,
                    TokenHash = rawToken == null ? null : ParentActivationTokens.Hash(rawToken),
                    ExpiresAt = now + ParentInvitationRules.Validity,
                    Status = ParentInvitationStatus.Pending,
                    IsNewAccount = isNewAccount,
                    SendCount = 1,
                    LastSentAt = now,
                    CreatedAt = now
                };
                _context.ParentInvitations.Add(invitation);
                activationUrl = rawToken == null ? null : ActivationUrl(rawToken);
                AddInvitationEvent(invitation, student.KeycloakId, FirstNameOf(student.FullName), activationUrl, now);
                await _context.SaveChangesAsync(token);
                return true;
            }, ct);
        }
        catch (Exception ex) when (ex is ParentLinkLockTimeoutException or DbUpdateException)
        {
            _logger.LogWarning(ex, "[ParentInvitations] Davet yazılamadı (kilit/yarış): studentId={StudentId}", studentId);
            error = ParentInvitationErrorCodes.Busy;
        }

        _status.Invalidate(student.UserId);
        if (error != null)
            return Fail(error, conflict: true);

        if (activationUrl != null)
            _devLinks.Remember(invitation!.Id, activationUrl, invitation.ExpiresAt);

        _logger.LogInformation(
            "[ParentInvitations] Veli daveti oluşturuldu: studentId={StudentId} invitationId={InvitationId} newAccount={IsNew} superseded={Superseded}",
            studentId, invitation!.Id, isNewAccount, superseded);
        return await OkAsync(student.UserId, "parentInvitations.sent", ct);
    }

    public async Task DiscardNewStudentAsync(int studentUserId, CancellationToken ct = default)
    {
        try
        {
            var deleted = await _context.Students.IgnoreQueryFilters()
                .Where(s => s.UserId == studentUserId
                            && !_context.ParentStudentLinks.Any(l => l.StudentId == s.Id)
                            && !_context.ParentInvitations.Any(i => i.StudentId == s.Id))
                .ExecuteDeleteAsync(CancellationToken.None);
            _status.Invalidate(studentUserId);
            if (deleted > 0)
                _logger.LogWarning("[ParentInvitations] Davet yazılamadı; bu kayıtta açılan öğrenci satırı geri alındı: userId={UserId}", studentUserId);
        }
        catch (Exception ex) when (ex is DbUpdateException or Npgsql.PostgresException)
        {
            // FK (ör. eşzamanlı başka yazım; ExecuteDelete PostgresException fırlatır) → satır kalır; öğrenci kaydı yarım (rolsüz) ama tekrar denenebilir.
            _logger.LogError(ex, "[ParentInvitations] Kayıt geri alınamadı: userId={UserId}", studentUserId);
        }
    }

    public async Task<ParentInvitationResultDto> ResendAsync(ParentInvitationStudent student, bool requiresParent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(student);
        if (!requiresParent)
            return Fail(ParentInvitationErrorCodes.ParentNotRequired);

        var facts = await StudentParentStatusService.LoadFactsAsync(_context, student.UserId, Now, ct);
        if (facts == null)
            return Fail(ParentLinkErrorCodes.ProfileNotFound, notFound: true);
        if (facts.HasActiveParent)
            return Fail(ParentInvitationErrorCodes.ParentAlreadyLinked, conflict: true);
        if (facts.Invitation == null)
            return Fail(ParentInvitationErrorCodes.NoOpenInvitation, notFound: true);

        return await ResendCoreAsync(facts.Invitation.Id, student, contact: null, ct);
    }

    /// <summary>
    /// Ortak yeniden gönderim (öğrenci ya da veli token'ıyla): sınır kontrolü → koşullu UPDATE (eşzamanlı ikinci gönderim kaybeder) +
    /// event aynı transaction'da. Yeni hesapta yeni token (eski bağlantı geçersiz), süre yeniden 7 gün.
    /// </summary>
    private async Task<ParentInvitationResultDto> ResendCoreAsync(
        Guid invitationId, ParentInvitationStudent? student, ParentContactValidation? contact, CancellationToken ct)
    {
        var now = Now;
        var inv = await _context.ParentInvitations.AsNoTracking()
            .Where(i => i.Id == invitationId)
            .Where(StudentParentStatusService.IsOpenInvitation)
            .Select(i => new { i.Id, i.StudentId, StudentUserId = i.Student.UserId, i.Email, i.IsNewAccount, i.SendCount, i.UsedAt })
            .FirstOrDefaultAsync(ct);
        if (inv == null)
            return Fail(ParentInvitationErrorCodes.NoOpenInvitation, notFound: true);
        if (inv.UsedAt is DateTime claimedAt && AsUtc(claimedAt) > now - ParentInvitationRules.ClaimStaleAfter)
            return Fail(ParentInvitationErrorCodes.Busy, conflict: true); // aktivasyon sürüyor (bayat talep bırakılmış sayılır)

        var history = await StudentParentStatusService.SendHistoryAsync(_context, inv.StudentId, inv.Email, ct);
        var decision = ParentInvitationLimits.Resend(history.Total, history.LastSent, now);
        if (!decision.Allowed)
            return RateLimited(ParentInvitationErrorCodes.ParentInvitationResendLimited, decision.NextResendAt, now);

        // Bildirim alanları: öğrenci isteğinde token'dan; veli isteğinde auth-api (fail-soft).
        var studentKeycloakId = student?.KeycloakId;
        var studentFirstName = student == null ? null : FirstNameOf(student.FullName);
        if (student == null)
        {
            var users = await ParentNotificationSupport.LookupAsync(_authApi, new[] { inv.StudentUserId }, _logger, ct);
            var u = ParentNotificationSupport.Of(users, inv.StudentUserId);
            studentKeycloakId = u.KeycloakId;
            studentFirstName = FirstNameOf(u.DisplayName);
        }

        var rawToken = inv.IsNewAccount ? ParentActivationTokens.Generate() : null;
        var newHash = rawToken == null ? null : ParentActivationTokens.Hash(rawToken);
        var expiresAt = now + ParentInvitationRules.Validity;
        var activationUrl = rawToken == null ? null : ActivationUrl(rawToken);
        var sent = false;
        try
        {
            await ParentLinkPrimary.InStudentLockAsync(_context, inv.StudentId, async token =>
            {
                sent = false;
                var affected = await _context.ParentInvitations
                    .Where(i => i.Id == inv.Id && i.Status == ParentInvitationStatus.Pending && i.SendCount == inv.SendCount
                                && (i.UsedAt == null || i.UsedAt <= now - ParentInvitationRules.ClaimStaleAfter))
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(i => i.UsedAt, (DateTime?)null)
                        .SetProperty(i => i.TokenHash, newHash)
                        .SetProperty(i => i.ExpiresAt, expiresAt)
                        .SetProperty(i => i.SendCount, inv.SendCount + 1)
                        .SetProperty(i => i.LastSentAt, now), token);
                if (affected == 0)
                    return false;
                if (contact != null)
                {
                    await _context.ParentInvitations.Where(i => i.Id == inv.Id)
                        .ExecuteUpdateAsync(set => set.SetProperty(i => i.FullName, contact.FullName).SetProperty(i => i.Phone, contact.Phone), token);
                }

                var row = await _context.ParentInvitations.AsNoTracking().FirstAsync(i => i.Id == inv.Id, token);
                AddInvitationEvent(row, studentKeycloakId ?? string.Empty, studentFirstName ?? string.Empty, activationUrl, now);
                await _context.SaveChangesAsync(token);
                sent = true;
                return true;
            }, ct);
        }
        catch (Exception ex) when (ex is ParentLinkLockTimeoutException or DbUpdateException)
        {
            _logger.LogWarning(ex, "[ParentInvitations] Yeniden gönderim yazılamadı: invitationId={InvitationId}", inv.Id);
            return Fail(ParentInvitationErrorCodes.Busy, conflict: true);
        }

        _status.Invalidate(inv.StudentUserId);
        if (!sent)
            return Fail(ParentInvitationErrorCodes.Busy, conflict: true);

        if (activationUrl != null)
            _devLinks.Remember(inv.Id, activationUrl, expiresAt);
        _logger.LogInformation("[ParentInvitations] Davet yeniden gönderildi: invitationId={InvitationId} sendCount={SendCount}",
            inv.Id, inv.SendCount + 1);
        return await OkAsync(inv.StudentUserId, "parentInvitations.resent", ct);
    }

    public async Task<ParentStatusDto> GetStatusAsync(int studentUserId, bool requiresParent, bool stamp, CancellationToken ct = default)
    {
        var state = stamp
            ? await _status.RefreshAndStampAsync(studentUserId, requiresParent, ct)
            : await _status.GetAsync(studentUserId, requiresParent, ct);
        return ToDto(state, Now);
    }

    // ---- anonim: aktivasyon ------------------------------------------------------------------------------------------

    private sealed record TokenInvitation(
        Guid Id, string TokenHash, int StudentId, int StudentUserId, int? LinkId, string Email, string FullName, bool IsNewAccount,
        ParentInvitationStatus Status, DateTime? UsedAt, DateTime ExpiresAt, ParentStudentLinkStatus? LinkStatus)
    {
        /// <summary>
        /// Kapalı: Pending değil, bağlantısı Pending değil ya da TAZE bir talep var (aktivasyon sürüyor). Security MINOR3: 5 dakikadan eski
        /// talep (çökme / yakalanmamış hata sonrası kalan UsedAt) bırakılmış sayılır — token yeniden kullanılabilir.
        /// </summary>
        public bool IsClosed(DateTime now) => Status != ParentInvitationStatus.Pending
                                              || (UsedAt is DateTime used && DateTime.SpecifyKind(used, DateTimeKind.Utc) > now - ParentInvitationRules.ClaimStaleAfter)
                                              || (LinkId != null && LinkStatus != ParentStudentLinkStatus.Pending);
    }

    private async Task<TokenInvitation?> FindByTokenAsync(string? token, CancellationToken ct)
    {
        var hash = ParentActivationTokens.Hash(token);
        if (hash == null)
            return null;
        return await _context.ParentInvitations.AsNoTracking()
            .Where(i => i.TokenHash == hash)
            .Select(i => new TokenInvitation(i.Id, hash, i.StudentId, i.Student.UserId, i.LinkId, i.Email, i.FullName,
                i.IsNewAccount, i.Status, i.UsedAt, i.ExpiresAt, i.LinkId == null ? null : i.Link!.Status))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ParentActivationOutcomeDto> LookupAsync(string? token, CancellationToken ct = default)
    {
        var inv = await FindByTokenAsync(token, ct);
        var now = Now;
        // Security MINOR6: düzeltilmiş/iptal edilmiş davet ve 1 günden eski kullanılmış token bilinmeyen token gibi (404) — eski bağlantı
        // davet geçmişi hakkında bilgi vermesin. Yeni kullanılmış token kısa süre "zaten kullanıldı" diye tanınır.
        if (inv == null
            || inv.Status is ParentInvitationStatus.Superseded or ParentInvitationStatus.Cancelled
            || (inv.LinkId != null && inv.LinkStatus == ParentStudentLinkStatus.Revoked)
            || (inv.Status == ParentInvitationStatus.Activated
                && (inv.UsedAt == null || AsUtc(inv.UsedAt.Value) + ParentInvitationRules.UsedTokenRecognition <= now)))
            return FailActivation(ParentInvitationErrorCodes.InvalidActivationToken, notFound: true);

        var users = await ParentNotificationSupport.LookupAsync(_authApi, new[] { inv.StudentUserId }, _logger, ct);
        var history = await StudentParentStatusService.SendHistoryAsync(_context, inv.StudentId, inv.Email, ct);
        // Security C1: e-postaya ait hesap varsa sayfa doğrudan "giriş yapıp kabul et"e gider (yalnız token sahibine; fail-soft false).
        var accountExists = false;
        if (inv.IsNewAccount && !inv.IsClosed(Now))
        {
            var account = await _directory.LookupAsync(inv.Email, ct);
            accountExists = account is { Status: ParentAccountCallStatus.Success, Lookup.Outcome: not ParentAccountOutcomes.None };
        }
        return new ParentActivationOutcomeDto
        {
            Success = true,
            Lookup = new ParentActivationLookupDto
            {
                StudentName = ParentNotificationSupport.Of(users, inv.StudentUserId).DisplayName,
                MaskedParentEmail = EmailMask.ApplyWithDomain(inv.Email),
                Kind = inv.IsNewAccount ? KindNewParentActivation : KindExistingParentApproval,
                ExpiresAt = AsUtc(inv.ExpiresAt),
                Expired = AsUtc(inv.ExpiresAt) <= now,
                Used = inv.IsClosed(Now),
                RequiresPasswordSetup = inv.IsNewAccount && !accountExists,
                AccountExists = accountExists,
                ConsentVersion = ParentConsentVersions.Current,
                ConsentTextKey = ConsentTextKey,
                CanResend = !inv.IsClosed(Now) && ParentInvitationLimits.Resend(history.Total, history.LastSent, now).Allowed
            }
        };
    }

    public async Task<ParentActivationOutcomeDto> ActivateAsync(ParentActivationRequestDto? request, string? ipAddress, CancellationToken ct = default)
    {
        if (request is not { ConsentAccepted: true })
            return FailActivation(ParentInvitationErrorCodes.ConsentRequired);
        if (!string.Equals(request.ConsentVersion?.Trim(), ParentConsentVersions.Current, StringComparison.Ordinal))
            return FailActivation(ParentInvitationErrorCodes.ConsentVersionMismatch);

        var inv = await FindByTokenAsync(request.Token, ct);
        if (inv == null || !inv.IsNewAccount)
            return FailActivation(ParentInvitationErrorCodes.InvalidActivationToken, notFound: true);
        if (inv.IsClosed(Now))
            return FailActivation(ParentInvitationErrorCodes.ActivationTokenUsed, conflict: true);
        var now = Now;
        if (AsUtc(inv.ExpiresAt) <= now)
            return FailActivation(ParentInvitationErrorCodes.ActivationTokenExpired, conflict: true);
        if (!ChildAccountRules.IsValidPassword(request.Password))
            return FailActivation(ParentInvitationErrorCodes.PasswordRejected);

        // Tek kullanım: önce koşullu talep (UsedAt) — token hash'i de koşulda (code MINOR4: arada yeniden gönderilip token değiştiyse eski
        // token talep edemez). Başarısız auth-api/yerel yazımda talep geri bırakılır (token yeniden kullanılabilir).
        var claimed = await _context.ParentInvitations
            .Where(i => i.Id == inv.Id && i.TokenHash == inv.TokenHash && i.Status == ParentInvitationStatus.Pending
                        && (i.UsedAt == null || i.UsedAt <= now - ParentInvitationRules.ClaimStaleAfter) && i.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.UsedAt, now), CancellationToken.None);
        if (claimed == 0)
            return FailActivation(ParentInvitationErrorCodes.ActivationTokenUsed, conflict: true);

        // Security MINOR3: talepten sonraki her beklenmeyen hata talebi bırakır (token kilitli kalmasın), sonra yeniden fırlatır.
        try
        {
            return await ActivateClaimedAsync(inv, request, ipAddress, now, ct);
        }
        catch
        {
            await ReleaseClaimAsync(inv.Id, now);
            throw;
        }
    }

    private async Task<ParentActivationOutcomeDto> ActivateClaimedAsync(
        TokenInvitation inv, ParentActivationRequestDto request, string? ipAddress, DateTime now, CancellationToken ct)
    {
        // Security M2: hesap ancak burada (parola belirlenirken) açılır.
        int parentUserId;
        const bool passwordSet = true;
        var created = await _directory.CreateAsync(inv.Email, inv.FullName, request.Password!, CancellationToken.None);
        switch (created.Status)
        {
            case ParentAccountCallStatus.Success:
                parentUserId = created.Created!.UserId;
                break;
            case ParentAccountCallStatus.Conflict:
                // Security C1: e-postaya ait hesap var — anonim aktivasyon ASLA bağlantı kurmaz (doğrulanmamış e-postayla ele geçirilmiş bir
                // hesaba bağlanabilirdi). Veli o hesapla giriş yapıp daveti kabul eder (AcceptAsync: oturum + token + sunucu e-postası).
                await ReleaseClaimAsync(inv.Id, now);
                return FailActivation(ParentInvitationErrorCodes.LoginRequired, conflict: true);
            case ParentAccountCallStatus.Invalid when created.ErrorCode == ParentAccountErrorCodes.PasswordRejected:
                await ReleaseClaimAsync(inv.Id, now);
                return FailActivation(ParentInvitationErrorCodes.PasswordRejected);
            default:
                await ReleaseClaimAsync(inv.Id, now);
                _logger.LogError("[ParentInvitations] Aktivasyon: veli hesabı açılamadı (durum={Status}, kod={Code}, invitationId={InvitationId}).",
                    created.Status, created.ErrorCode, inv.Id);
                return UpstreamActivation();
        }

        var failure = await CompleteLinkAsync(inv.Id, inv.LinkId, inv.StudentId, inv.StudentUserId, parentUserId, ipAddress, ct);
        if (failure != null)
        {
            // Hesap açıldıysa kalır: bir sonraki anonim deneme 409 LoginRequired alır; veli o hesapla giriş yapıp kabul eder (AcceptAsync).
            await ReleaseClaimAsync(inv.Id, now);
            return FailActivation(failure.Code, notFound: failure.NotFound, conflict: failure.Conflict);
        }

        _logger.LogInformation("[ParentInvitations] Veli aktive edildi, bağlantı Active: invitationId={InvitationId} passwordSet={PasswordSet}",
            inv.Id, passwordSet);
        return new ParentActivationOutcomeDto
        {
            Success = true,
            Message = _localizer["parentInvitations.activated"],
            Activation = new ParentActivationResultDto { Activated = true, PasswordSet = passwordSet }
        };
    }

    public async Task<ParentActivationOutcomeDto> ResendByTokenAsync(string? token, CancellationToken ct = default)
    {
        var inv = await FindByTokenAsync(token, ct);
        if (inv == null || inv.Status is ParentInvitationStatus.Superseded or ParentInvitationStatus.Cancelled)
            return FailActivation(ParentInvitationErrorCodes.InvalidActivationToken, notFound: true);
        if (inv.IsClosed(Now))
            return FailActivation(ParentInvitationErrorCodes.ActivationTokenUsed, conflict: true);

        var result = await ResendCoreAsync(inv.Id, student: null, contact: null, ct);
        // Anonim çağırana öğrencinin durumu dönmez.
        return new ParentActivationOutcomeDto
        {
            Success = result.Success,
            Message = result.Success ? _localizer["parentInvitations.resentToParent"] : result.Message,
            ErrorCode = result.ErrorCode,
            NotFound = result.NotFound,
            Conflict = result.Conflict,
            RateLimited = result.RateLimited,
            RetryAfterSeconds = result.RetryAfterSeconds,
            RetryAt = result.RetryAt
        };
    }

    public async Task<ParentActivationOutcomeDto> AcceptAsync(
        ParentInvitationAcceptRequestDto? request, int callerUserId, string? ipAddress, CancellationToken ct = default)
    {
        if (request is not { ConsentAccepted: true })
            return FailActivation(ParentInvitationErrorCodes.ConsentRequired);
        if (!string.Equals(request.ConsentVersion?.Trim(), ParentConsentVersions.Current, StringComparison.Ordinal))
            return FailActivation(ParentInvitationErrorCodes.ConsentVersionMismatch);
        if (callerUserId <= 0)
            return FailActivation(ParentInvitationErrorCodes.ParentAccountRequired, forbidden: true);

        var inv = await FindByTokenAsync(request.Token, ct);
        if (inv == null || !inv.IsNewAccount || inv.Status is ParentInvitationStatus.Superseded or ParentInvitationStatus.Cancelled)
            return FailActivation(ParentInvitationErrorCodes.InvalidActivationToken, notFound: true);
        if (inv.IsClosed(Now))
            return FailActivation(ParentInvitationErrorCodes.ActivationTokenUsed, conflict: true);
        var now = Now;
        if (AsUtc(inv.ExpiresAt) <= now)
            return FailActivation(ParentInvitationErrorCodes.ActivationTokenExpired, conflict: true);

        var claimed = await _context.ParentInvitations
            .Where(i => i.Id == inv.Id && i.TokenHash == inv.TokenHash && i.Status == ParentInvitationStatus.Pending
                        && (i.UsedAt == null || i.UsedAt <= now - ParentInvitationRules.ClaimStaleAfter) && i.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.UsedAt, now), CancellationToken.None);
        if (claimed == 0)
            return FailActivation(ParentInvitationErrorCodes.ActivationTokenUsed, conflict: true);

        try
        {
            return await AcceptClaimedAsync(inv, callerUserId, ipAddress, now, ct);
        }
        catch
        {
            await ReleaseClaimAsync(inv.Id, now);
            throw;
        }
    }

    private async Task<ParentActivationOutcomeDto> AcceptClaimedAsync(
        TokenInvitation inv, int callerUserId, string? ipAddress, DateTime now, CancellationToken ct)
    {
        // Hesap sahipliği (oturum) + e-posta sahipliği (token) + hesabın SUNUCUDAKİ e-postası davet e-postası (JWT claim'ine güvenilmez).
        var confirmed = await _directory.ConfirmEmailAsync(callerUserId, inv.Email, CancellationToken.None);
        if (confirmed.Status != ParentAccountCallStatus.Success)
        {
            await ReleaseClaimAsync(inv.Id, now);
            if (confirmed.Status == ParentAccountCallStatus.Conflict && confirmed.ErrorCode == ParentAccountErrorCodes.EmailMismatch)
            {
                _logger.LogWarning("[ParentInvitations] Davet kabulü reddedildi: hesap e-postası davet e-postası değil (invitationId={InvitationId}, userId={UserId}).",
                    inv.Id, callerUserId);
                return FailActivation(ParentInvitationErrorCodes.InvitationEmailMismatch, forbidden: true);
            }
            if (confirmed.Status is ParentAccountCallStatus.Conflict or ParentAccountCallStatus.Invalid)
                return FailActivation(ParentInvitationErrorCodes.ParentAccountRequired, forbidden: true);
            return UpstreamActivation();
        }

        var failure = await CompleteLinkAsync(inv.Id, inv.LinkId, inv.StudentId, inv.StudentUserId, callerUserId, ipAddress, ct);
        if (failure != null)
        {
            await ReleaseClaimAsync(inv.Id, now);
            return FailActivation(failure.Code, notFound: failure.NotFound, conflict: failure.Conflict);
        }

        _logger.LogInformation("[ParentInvitations] Oturum açmış veli daveti kabul etti: invitationId={InvitationId} userId={UserId}",
            inv.Id, callerUserId);
        return new ParentActivationOutcomeDto
        {
            Success = true,
            Message = _localizer["parentInvitations.activated"],
            Activation = new ParentActivationResultDto { Activated = true, PasswordSet = false }
        };
    }

    // ---- mevcut veli: onay / ret --------------------------------------------------------------------------------------

    private sealed record InvitedParentLink(int LinkId, ParentStudentLinkStatus Status, int StudentId, int StudentUserId, int ParentId);

    private async Task<InvitedParentLink?> FindInvitedLinkAsync(int linkId, int parentUserId, CancellationToken ct)
        => await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Id == linkId && l.Origin == ParentStudentLinkOrigin.StudentRegistered
                        && l.Parent.UserId == parentUserId && !l.Parent.IsDeleted && !l.Student.IsDeleted)
            .Select(l => new InvitedParentLink(l.Id, l.Status, l.StudentId, l.Student.UserId, l.ParentId))
            .FirstOrDefaultAsync(ct);

    public async Task<ParentLinkResponseDto?> ApproveAsInvitedParentAsync(
        int linkId, int parentUserId, ParentLinkApproveRequestDto? consent, string? ipAddress, CancellationToken ct = default)
    {
        var link = await FindInvitedLinkAsync(linkId, parentUserId, ct);
        if (link == null)
            return null;
        if (link.Status == ParentStudentLinkStatus.Active)
            return OkLink(link.LinkId, "parentLinks.approved");
        if (link.Status != ParentStudentLinkStatus.Pending)
            return FailLink(ParentLinkErrorCodes.NotFound, notFound: true);

        var inv = await _context.ParentInvitations.AsNoTracking()
            .Where(i => i.LinkId == link.LinkId && i.Status == ParentInvitationStatus.Pending)
            .Select(i => new { i.Id, i.ExpiresAt })
            .FirstOrDefaultAsync(ct);
        if (inv == null)
            return FailLink(ParentLinkErrorCodes.NotFound, notFound: true);

        if (consent is not { ConsentAccepted: true })
            return FailLink(ParentInvitationErrorCodes.ConsentRequired);
        if (!string.Equals(consent.ConsentVersion?.Trim(), ParentConsentVersions.Current, StringComparison.Ordinal))
            return FailLink(ParentInvitationErrorCodes.ConsentVersionMismatch);
        if (AsUtc(inv.ExpiresAt) <= Now)
            return FailLink(ParentInvitationErrorCodes.InvitationExpired, conflict: true);

        var failure = await CompleteLinkAsync(inv.Id, link.LinkId, link.StudentId, link.StudentUserId, parentUserId, ipAddress, ct);
        if (failure != null)
            return FailLink(failure.Code, notFound: failure.NotFound, conflict: failure.Conflict);

        _logger.LogInformation("[ParentInvitations] Mevcut veli öğrenci davetini onayladı: linkId={LinkId}", link.LinkId);
        return OkLink(link.LinkId, "parentLinks.approved");
    }

    public async Task<ParentLinkResponseDto?> RejectAsInvitedParentAsync(int linkId, int parentUserId, CancellationToken ct = default)
    {
        var link = await FindInvitedLinkAsync(linkId, parentUserId, ct);
        if (link == null)
            return null;
        if (link.Status == ParentStudentLinkStatus.Revoked)
            return OkLink(link.LinkId, "parentLinks.rejected");
        if (link.Status != ParentStudentLinkStatus.Pending)
            return FailLink(ParentLinkErrorCodes.NotFound, notFound: true);

        try
        {
            await ParentLinkPrimary.InStudentLockAsync(_context, link.StudentId, async token =>
            {
                var now = Now;
                var affected = await _context.ParentStudentLinks
                    .Where(l => l.Id == link.LinkId && l.Status == ParentStudentLinkStatus.Pending)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                        .SetProperty(l => l.RevokedAt, now)
                        .SetProperty(l => l.RevokedByUserId, parentUserId), token);
                if (affected == 0)
                    return false;
                await _context.ParentInvitations
                    .Where(i => i.LinkId == link.LinkId && i.Status == ParentInvitationStatus.Pending)
                    .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, ParentInvitationStatus.Cancelled)
                        .SetProperty(i => i.ClosedAt, now), token);
                return true;
            }, ct);
        }
        catch (ParentLinkLockTimeoutException)
        {
            return FailLink(ParentLinkErrorCodes.Busy, conflict: true);
        }

        _status.Invalidate(link.StudentUserId);
        _logger.LogInformation("[ParentInvitations] Veli öğrenci davetini reddetti: linkId={LinkId}", link.LinkId);
        return OkLink(link.LinkId, "parentLinks.rejected");
    }

    // ---- ortak: bağlantıyı Active yap ----------------------------------------------------------------------------------

    private sealed record CompletionFailure(string Code, bool NotFound = false, bool Conflict = false);

    /// <summary>
    /// Öğrenci kilidi (+ veli kilidi) altında: Parent satırı (yoksa) → tavanlar → mevcut Pending bağlantı Active ya da (yeni velide) yeni
    /// Active bağlantı → davet Activated (+ veli kimlikleri) → birincil onarımı → sayaç temizliği → rıza → ParentLinkedEvent. null = başarı.
    /// </summary>
    private async Task<CompletionFailure?> CompleteLinkAsync(
        Guid invitationId, int? existingLinkId, int studentId, int studentUserId, int parentUserId, string? ipAddress, CancellationToken ct)
    {
        var notifyUsers = await ParentNotificationSupport.LookupAsync(_authApi, new[] { parentUserId, studentUserId }, _logger, ct);
        var parentNotify = ParentNotificationSupport.Of(notifyUsers, parentUserId);
        var studentNotify = ParentNotificationSupport.Of(notifyUsers, studentUserId);

        CompletionFailure? failure = null;
        try
        {
            await ParentLinkPrimary.InStudentLockAsync(_context, studentId, async token =>
            {
                failure = null;
                var now = Now;

                var parentId = await EnsureParentRowAsync(parentUserId, token);
                await _context.Database.AcquireParentChildLinkLockAsync(parentId, token);

                var activeParents = await _context.ParentStudentLinks.CountAsync(l => l.StudentId == studentId
                    && l.Status == ParentStudentLinkStatus.Active && !l.Parent.IsDeleted, token);
                if (activeParents >= ParentLinkRules.MaxActiveParentsPerStudent)
                {
                    failure = new CompletionFailure(ParentLinkErrorCodes.StudentLimitReached, Conflict: true);
                    return false;
                }
                var activeChildren = await _context.ParentStudentLinks.CountAsync(l => l.ParentId == parentId
                    && l.Status == ParentStudentLinkStatus.Active && !l.Student.IsDeleted, token);
                if (activeChildren >= ParentLinkRules.MaxActiveChildrenPerParent)
                {
                    failure = new CompletionFailure(ParentLinkErrorCodes.ParentLimitReached, Conflict: true);
                    return false;
                }

                int linkId;
                if (existingLinkId is int existing)
                {
                    var affected = await _context.ParentStudentLinks
                        .Where(l => l.Id == existing && l.Status == ParentStudentLinkStatus.Pending && l.Origin == ParentStudentLinkOrigin.StudentRegistered)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(l => l.Status, ParentStudentLinkStatus.Active)
                            .SetProperty(l => l.ActivatedAt, now), token);
                    if (affected == 0)
                    {
                        failure = new CompletionFailure(ParentInvitationErrorCodes.ActivationTokenUsed, Conflict: true);
                        return false;
                    }
                    linkId = existing;
                }
                else
                {
                    if (await _context.ParentStudentLinks.AnyAsync(l => l.ParentId == parentId && l.StudentId == studentId
                            && l.Status == ParentStudentLinkStatus.Active, token))
                    {
                        failure = new CompletionFailure(ParentLinkErrorCodes.AlreadyLinked, Conflict: true);
                        return false;
                    }
                    // Aynı çiftin bekleyen başka bağlantısı (ör. ikinci veli isteği) yeni Active satırı engellemesin (çift başına tek açık).
                    await _context.ParentStudentLinks
                        .Where(l => l.ParentId == parentId && l.StudentId == studentId && l.Status == ParentStudentLinkStatus.Pending)
                        .ExecuteUpdateAsync(set => set.SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                            .SetProperty(l => l.RevokedAt, now), token);
                    var link = new ParentStudentLink
                    {
                        ParentId = parentId,
                        StudentId = studentId,
                        Status = ParentStudentLinkStatus.Active,
                        Origin = ParentStudentLinkOrigin.StudentRegistered,
                        IsPrimary = false,
                        CreatedAt = now,
                        ActivatedAt = now
                    };
                    _context.ParentStudentLinks.Add(link);
                    await _context.SaveChangesAsync(token);
                    linkId = link.Id;
                }

                var invitationClosed = await _context.ParentInvitations
                    .Where(i => i.Id == invitationId && i.Status == ParentInvitationStatus.Pending)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(i => i.Status, ParentInvitationStatus.Activated)
                        .SetProperty(i => i.UsedAt, now)
                        .SetProperty(i => i.ClosedAt, now)
                        .SetProperty(i => i.ParentUserId, parentUserId)
                        .SetProperty(i => i.ParentKeycloakId, parentNotify.KeycloakId == string.Empty ? null : parentNotify.KeycloakId)
                        .SetProperty(i => i.ParentId, parentId)
                        .SetProperty(i => i.LinkId, linkId), token);
                if (invitationClosed == 0)
                {
                    failure = new CompletionFailure(ParentInvitationErrorCodes.ActivationTokenUsed, Conflict: true);
                    return false;
                }

                await ParentLinkPrimary.EnsurePrimaryAsync(_context, studentId, token);
                await _context.Students.Where(s => s.Id == studentId && s.ParentRequirementStartedAt != null)
                    .ExecuteUpdateAsync(set => set.SetProperty(s => s.ParentRequirementStartedAt, (DateTime?)null), token);

                _context.ParentConsents.Add(new ParentConsent
                {
                    ParentId = parentId,
                    StudentId = studentId,
                    Version = ParentConsentVersions.Current,
                    AcceptedAt = now,
                    IpAddress = ipAddress
                });
                _context.OutboxMessages.Add(new OutboxMessage
                {
                    Type = OutboxEventRegistry.NameFor<ParentLinkedEvent>(),
                    Content = JsonSerializer.Serialize(new ParentLinkedEvent
                    {
                        EventId = Guid.NewGuid(),
                        LinkId = linkId,
                        ParentId = parentId,
                        ParentUserId = parentUserId,
                        StudentId = studentId,
                        StudentUserId = studentUserId,
                        ParentKeycloakId = parentNotify.KeycloakId,
                        StudentKeycloakId = studentNotify.KeycloakId,
                        ParentDisplayName = parentNotify.DisplayName,
                        StudentDisplayName = studentNotify.DisplayName,
                        LinkedAtUtc = now
                    }),
                    CreatedAt = now
                });
                await _context.SaveChangesAsync(token);
                return true;
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is ParentLinkLockTimeoutException or DbUpdateException)
        {
            _logger.LogWarning(ex, "[ParentInvitations] Bağlantı Active yapılamadı (kilit/yarış): invitationId={InvitationId}", invitationId);
            failure = new CompletionFailure(ParentInvitationErrorCodes.Busy, Conflict: true);
        }

        _status.Invalidate(studentUserId);
        return failure;
    }

    // ---- yardımcılar ------------------------------------------------------------------------------------------------

    private async Task CloseInvitationAsync(Guid invitationId, int? linkId, ParentInvitationStatus status, int actorUserId, DateTime now, CancellationToken ct)
    {
        await _context.ParentInvitations.Where(i => i.Id == invitationId && i.Status == ParentInvitationStatus.Pending)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.Status, status).SetProperty(i => i.ClosedAt, now), ct);
        if (linkId is int id)
        {
            await _context.ParentStudentLinks.Where(l => l.Id == id && l.Status == ParentStudentLinkStatus.Pending)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                    .SetProperty(l => l.RevokedAt, now)
                    .SetProperty(l => l.RevokedByUserId, actorUserId), ct);
        }
    }

    /// <summary>Velinin exam Parent satırı (yoksa açılır) — veli kullanıcı anahtarında kayıt kilidi altında (çift satır olmasın).</summary>
    private async Task<int> EnsureParentRowAsync(int parentUserId, CancellationToken ct)
    {
        await _context.Database.AcquireUserRegistrationLockAsync(parentUserId, ct);
        var existing = await _context.Parents.AsNoTracking().Where(p => p.UserId == parentUserId)
            .OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);
        if (existing is int id)
            return id;
        var parent = new Parent { UserId = parentUserId };
        _context.Parents.Add(parent);
        await _context.SaveChangesAsync(ct);
        return parent.Id;
    }

    private async Task ReleaseClaimAsync(Guid invitationId, DateTime claimedAt)
    {
        try
        {
            await _context.ParentInvitations
                .Where(i => i.Id == invitationId && i.Status == ParentInvitationStatus.Pending && i.UsedAt == claimedAt)
                .ExecuteUpdateAsync(set => set.SetProperty(i => i.UsedAt, (DateTime?)null), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ParentInvitations] Aktivasyon talebi geri bırakılamadı: invitationId={InvitationId}", invitationId);
        }
    }

    /// <summary>
    /// Davet event'i: her gönderimde yeni <c>EventId</c>; aktivasyon bağlantısı outbox/RabbitMQ'da yalnız şifreli
    /// (<see cref="ActivationLinkProtector"/>, BadgeService çözer).
    /// </summary>
    private void AddInvitationEvent(ParentInvitation invitation, string studentKeycloakId, string studentFirstName, string? activationUrl, DateTime now)
    {
        _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<ParentInvitationRequestedEvent>(),
            Content = JsonSerializer.Serialize(new ParentInvitationRequestedEvent
            {
                EventId = Guid.NewGuid(),
                InvitationId = invitation.Id,
                SendCount = invitation.SendCount,
                StudentId = invitation.StudentId,
                StudentUserId = studentKeycloakId,
                StudentFirstName = studentFirstName,
                ParentUserId = invitation.ParentKeycloakId ?? string.Empty,
                ParentEmail = invitation.Email,
                ParentFullName = invitation.FullName,
                Kind = invitation.IsNewAccount ? KindNewParentActivation : KindExistingParentApproval,
                ProtectedActivationUrl = activationUrl == null ? null : _linkProtector.Protect(activationUrl),
                ExpiresAt = DateTime.SpecifyKind(invitation.ExpiresAt, DateTimeKind.Utc),
                OccurredAt = now
            }),
            CreatedAt = now
        });
    }

    /// <summary>Token URL FRAGMENT'ında (<c>#token=</c>): tarayıcı sunucuya göndermez — erişim/proxy loglarına düşmez.</summary>
    private string ActivationUrl(string rawToken)
    {
        var root = _options.CurrentValue.ActivationBaseUrl;
        if (string.IsNullOrWhiteSpace(root))
            root = _configuration?["Server:BaseUrl"];
        return $"{(root ?? string.Empty).TrimEnd('/')}/app/parent-activate#token={rawToken}";
    }

    private static string FirstNameOf(string? fullName)
    {
        var value = (fullName ?? string.Empty).Trim();
        var space = value.IndexOf(' ');
        return space > 0 ? value[..space] : value;
    }

    private ParentStatusDto ToDto(StudentParentState state, DateTime now)
    {
        var options = _options.CurrentValue;
        var dto = new ParentStatusDto
        {
            Mode = state.Mode.ToString(),
            RequiresParent = state.RequiresParent,
            GraceDays = options.GraceDays,
            GraceEndsAt = state.GraceEndsAt,
            DaysLeft = state.DaysLeft,
            IsFinalWarning = state.IsFinalWarning
        };
        // Expired'da da (süresi dolmuş) davet özeti döner — UI yeniden gönder / düzelt sunar.
        if (state.Mode == StudentParentMode.Ok || state.Facts?.Invitation is not { } inv)
            return dto;

        var decision = ParentInvitationLimits.Resend(inv.TotalSendsToEmail, inv.LastSentToEmail, now);
        dto.Invitation = new ParentInvitationSummaryDto
        {
            ParentName = inv.FullName,
            MaskedEmail = EmailMask.ApplyWithDomain(inv.Email),
            ExpiresAt = inv.ExpiresAt,
            Expired = inv.ExpiresAt <= now,
            CanResend = decision.Allowed,
            NextResendAt = decision.NextResendAt,
            ResendsLeft = decision.ResendsLeft,
            EmailChangeLockedUntil = ParentInvitationLimits.EmailChangeLockedUntil(state.Facts.RecentAttempts, null, now),
            DevActivationUrl = _devLinks.Get(inv.Id)
        };
        return dto;
    }

    private async Task<ParentInvitationResultDto> OkAsync(int studentUserId, string messageKey, CancellationToken ct) => new()
    {
        Success = true,
        Message = _localizer[messageKey],
        Status = await GetStatusAsync(studentUserId, requiresParent: true, stamp: false, ct)
    };

    private static ParentInvitationPrecheck Reject(ParentInvitationResultDto failure) => new(failure, null);

    private ParentInvitationResultDto Fail(string code, bool notFound = false, bool conflict = false) => new()
    {
        Success = false,
        NotFound = notFound,
        Conflict = conflict,
        ErrorCode = code,
        Message = MessageFor(code)
    };

    private ParentInvitationResultDto RateLimited(string code, DateTime? retryAt, DateTime now)
    {
        var dto = Fail(code);
        dto.RateLimited = true;
        dto.RetryAt = retryAt;
        dto.RetryAfterSeconds = retryAt is DateTime at ? Math.Max(1, (int)Math.Ceiling((at - now).TotalSeconds)) : 86_400;
        return dto;
    }

    private ParentInvitationResultDto Upstream()
    {
        var dto = Fail(ParentInvitationErrorCodes.Unavailable);
        dto.Upstream = true;
        return dto;
    }

    private ParentActivationOutcomeDto UpstreamActivation()
    {
        var dto = FailActivation(ParentInvitationErrorCodes.Unavailable);
        dto.Upstream = true;
        return dto;
    }

    private ParentActivationOutcomeDto FailActivation(string code, bool notFound = false, bool conflict = false, bool forbidden = false) => new()
    {
        Success = false,
        NotFound = notFound,
        Conflict = conflict,
        Forbidden = forbidden,
        ErrorCode = code,
        Message = MessageFor(code)
    };

    private ParentLinkResponseDto OkLink(int linkId, string messageKey) => new()
    {
        Success = true,
        ObjectId = linkId,
        Message = _localizer[messageKey]
    };

    private ParentLinkResponseDto FailLink(string code, bool notFound = false, bool conflict = false) => new()
    {
        Success = false,
        NotFound = notFound,
        Conflict = conflict,
        ErrorCode = code,
        Message = MessageFor(code)
    };

    private string MessageFor(string code)
    {
        var key = char.ToLowerInvariant(code[0]) + code[1..];
        if (code == ParentLinkErrorCodes.Busy)
            return _localizer["parentLinks.errors.busy"];
        return ParentLinkCodes.Contains(code) || code == ParentLinkErrorCodes.AlreadyLinked
            ? _localizer[$"parentLinks.errors.{key}"]
            : _localizer[$"parentInvitations.errors.{key}"];
    }

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
