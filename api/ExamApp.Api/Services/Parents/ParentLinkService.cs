using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Parents;

/// <inheritdoc cref="IParentLinkService"/>
public sealed class ParentLinkService : IParentLinkService
{
    /// <summary>Üretilen kodun kullanılmamış başka bir kodla hash çakışmasında yeniden deneme sayısı (pratikte hiç gerekmez).</summary>
    internal const int MaxGenerateAttempts = 5;

    /// <summary>Koparma/ret yarışında (eşzamanlı onay) durum yeniden okunarak en fazla bu kadar denenir.</summary>
    private const int MaxRevokeAttempts = 3;

    /// <summary><see cref="ParentUnlinkedEvent.RevokedByRole"/> değerleri.</summary>
    internal static class RevokedByRoles
    {
        public const string Student = "Student";

        /// <summary>Veli kendi bağlantısından ayrıldı (birincil ya da değil) ya da kendi isteğini iptal etti.</summary>
        public const string Parent = "Parent";

        /// <summary>Issue #436: birincil veli BAŞKA bir velinin bağlantısını kopardı.</summary>
        public const string PrimaryParent = "PrimaryParent";

        /// <summary>Issue #436: admin kopardı.</summary>
        public const string Admin = "Admin";
    }

    private enum ActorKind { Student, Parent, Admin }

    /// <summary>Velinin koparma isteği için karar (okuma anında ön kontrol + kilit altında kesin kontrol aynı kural).</summary>
    private enum RevokeDecision { Allow, Forbidden, LastParent }

    /// <summary>Koparma/ret isteği (tek gövde: <see cref="RevokeCoreAsync"/>).</summary>
    private sealed record RevokeRequest(
        int LinkId, int UserId, ActorKind Actor, bool PendingOnly, bool InviteCodeOnly, string OkMessageKey, string? AdminKeycloakId = null);

    private readonly AppDbContext _context;
    private readonly IParentInviteCodeHasher _hasher;
    private readonly IAuthApiClient _authApiClient;
    private readonly IParentRedeemAttemptGuard _guard;
    private readonly TimeProvider _time;
    private readonly ILogger<ParentLinkService>? _logger;
    private readonly IStringLocalizer<Messages> _localizer;

    /// <summary>Ad çözümü (auth-api) üst süresi; aşılırsa yerelleştirilmiş yedek ad. Liste yine döner.</summary>
    internal TimeSpan NameLookupTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public ParentLinkService(
        AppDbContext context,
        IParentInviteCodeHasher hasher,
        IAuthApiClient authApiClient,
        IParentRedeemAttemptGuard guard,
        TimeProvider? time = null,
        ILogger<ParentLinkService>? logger = null,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _hasher = hasher;
        _authApiClient = authApiClient;
        _guard = guard;
        _time = time ?? TimeProvider.System;
        _logger = logger;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ---- Öğrenci: salt okunur liste + geçiş dönemi onayı/reddi -------------------------------------------------------

    public async Task<StudentParentLinksDto?> GetStudentParentsAsync(int studentUserId, CancellationToken ct = default)
    {
        var studentId = await StudentIdOfAsync(studentUserId, ct);
        if (studentId == null)
            return null;

        var now = Now;
        var rows = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.StudentId == studentId)
            .Where(ParentLinkPrimary.IsOpen(now))
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Take(ParentLinkRules.MaxActiveParentsPerStudent)
            .Select(l => new
            {
                l.Id, l.Status, l.Origin, l.ParentId, l.IsPrimary, ParentUserId = l.Parent.UserId, l.ActivatedAt, l.CreatedAt
            })
            .ToListAsync(ct);

        var active = rows.Where(r => r.Status == ParentStudentLinkStatus.Active).ToList();
        // Yeni (#436) bekleyen istekler öğrenciye düşmez; yalnız geçiş dönemindeki #419 istekleri.
        var legacyPending = rows.Where(r => r.Status == ParentStudentLinkStatus.Pending && r.Origin == ParentStudentLinkOrigin.LegacyV1).ToList();
        var primary = ParentLinkPrimary.PickPrimary(active.Select(r =>
            new ParentLinkPrimary.ActiveLinkRow(r.Id, studentId.Value, r.ParentId, r.ParentUserId, r.IsPrimary, r.ActivatedAt ?? r.CreatedAt)));

        var users = await LookupUsersAsync(active.Concat(legacyPending).Select(r => r.ParentUserId), ct);
        return new StudentParentLinksDto
        {
            Items = active.Select(r => new LinkedParentDto
            {
                LinkId = r.Id,
                ParentName = NameOf(users, r.ParentUserId, "parentLinks.fallbackParentName"),
                LinkedAt = AsUtc(r.ActivatedAt ?? r.CreatedAt),
                IsPrimary = primary?.LinkId == r.Id
            }).ToList(),
            PendingRequests = legacyPending.Select(r => new PendingParentRequestDto
            {
                LinkId = r.Id,
                ParentName = NameOf(users, r.ParentUserId, "parentLinks.fallbackParentName"),
                // Re-review (#419): öğrenci isteği tanıyabilsin — yalnızca maskeli e-posta (a***@g***.com).
                ParentEmailMasked = MaskedEmailOf(users, r.ParentUserId),
                RequestedAt = AsUtc(r.CreatedAt),
                ExpiresAt = AsUtc(r.CreatedAt + ParentLinkRules.LegacyPendingValidity)
            }).ToList(),
            MaxActiveParents = ParentLinkRules.MaxActiveParentsPerStudent,
            RequiresParent = ParentRequirement.RequiresParentForStudentProfile()
        };
    }

    public async Task<ParentLinkResponseDto> ApproveLegacyAsync(int linkId, int studentUserId, CancellationToken ct = default)
    {
        // Yalnız geçiş dönemindeki #419 istekleri; yeni (#436) istek öğrenciye "yok" (404) — onay yolu kapalı.
        var target = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Id == linkId && l.Student.UserId == studentUserId && !l.Parent.IsDeleted
                        && l.Origin == ParentStudentLinkOrigin.LegacyV1)
            .Select(l => new { l.Id, l.Status, l.ParentId, l.StudentId, ParentUserId = l.Parent.UserId })
            .FirstOrDefaultAsync(ct);
        if (target == null)
            return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true);

        if (target.Status == ParentStudentLinkStatus.Active)
            return Ok(target.Id, "parentLinks.approved");

        return await ActivatePendingAsync(
            target.Id, target.ParentId, target.ParentUserId, target.StudentId, studentUserId,
            ParentStudentLinkOrigin.LegacyV1, authorize: null, ct);
    }

    public Task<ParentLinkResponseDto> RejectLegacyAsync(int linkId, int studentUserId, CancellationToken ct = default)
        => RevokeCoreAsync(new RevokeRequest(linkId, studentUserId, ActorKind.Student, PendingOnly: true, InviteCodeOnly: false,
            "parentLinks.rejected"), ct);

    // ---- Birincil veli: ikinci veli kodu / onay / ret ---------------------------------------------------------------

    public async Task<ParentInviteCodeResultDto> CreateSecondParentCodeAsync(int linkId, int parentUserId, CancellationToken ct = default)
    {
        var parentId = await ParentIdOfAsync(parentUserId, ct);
        if (parentId == null)
            return Fail<ParentInviteCodeResultDto>(ParentLinkErrorCodes.ProfileNotFound, notFound: true);

        // IDOR: yalnızca çağıranın KENDİ Active bağlantısı; başkasınınki / Pending / koparılmış → yok.
        var studentId = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Id == linkId && l.ParentId == parentId && l.Status == ParentStudentLinkStatus.Active
                        && !l.Parent.IsDeleted && !l.Student.IsDeleted)
            .Select(l => (int?)l.StudentId)
            .FirstOrDefaultAsync(ct);
        if (studentId == null)
            return Fail<ParentInviteCodeResultDto>(ParentLinkErrorCodes.NotFound, notFound: true);

        string? plainCode = null;
        DateTime expiresAt = default;
        string? error = null;

        try
        {
            await ParentLinkPrimary.InStudentLockAsync(_context, studentId.Value, async token =>
            {
                plainCode = null;
                error = null;
                var now = Now;

                // Kilit altında: birincil işaretini düzelt (silinen/koparılan birincil) ve çağıranın birincil olduğunu doğrula.
                var family = await ParentLinkPrimary.EnsurePrimaryAsync(_context, studentId.Value, token);
                if (family.Primary == null || family.Primary.ParentId != parentId || family.Primary.LinkId != linkId)
                {
                    // Arada koparıldıysa yok; hâlâ bağlı ama birincil değilse yasak.
                    error = family.Active.Any(r => r.LinkId == linkId)
                        ? ParentLinkErrorCodes.NotPrimaryParent
                        : ParentLinkErrorCodes.NotFound;
                    return true; // birincil onarımı kalsın
                }

                var openParents = await _context.ParentStudentLinks
                    .Where(l => l.StudentId == studentId)
                    .Where(ParentLinkPrimary.IsOpen(now))
                    .CountAsync(token);
                if (openParents >= ParentLinkRules.MaxActiveParentsPerStudent)
                {
                    error = ParentLinkErrorCodes.StudentLimitReached;
                    return true;
                }

                // Çocuk başına tek geçerli kod: öncekiler (eski öğrenci kodları dahil) "şimdi" bitmiş sayılır.
                await _context.ParentInviteCodes
                    .Where(c => c.StudentId == studentId && c.UsedAt == null && c.ExpiresAt > now)
                    .ExecuteUpdateAsync(set => set.SetProperty(c => c.ExpiresAt, now), token);

                // Kullanılmamış (süresi dolmuş olsa da) kodlar arasında hash tekil (filtreli unique index). Çakışma
                // denemeleri tükenirse hiçbir şey YAZILMAZ (geri alınır) — yeniden denenebilir hata döner (#419 review D2).
                string? code = null;
                string? hash = null;
                for (var attempt = 0; attempt < MaxGenerateAttempts; attempt++)
                {
                    var candidate = _hasher.Generate();
                    var candidateHash = _hasher.Hash(candidate);
                    if (await _context.ParentInviteCodes.AnyAsync(c => c.CodeHash == candidateHash && c.UsedAt == null, token))
                        continue;
                    code = candidate;
                    hash = candidateHash;
                    break;
                }

                if (code == null || hash == null)
                {
                    _logger?.LogError("[ParentLinks] {Attempts} denemede çakışmasız davet kodu üretilemedi (studentId={StudentId}).",
                        MaxGenerateAttempts, studentId);
                    error = ParentLinkErrorCodes.Busy;
                    return false;
                }

                expiresAt = now.Add(ParentLinkRules.CodeValidity);
                _context.ParentInviteCodes.Add(new ParentInviteCode
                {
                    StudentId = studentId.Value,
                    CodeHash = hash,
                    ExpiresAt = expiresAt,
                    CreatedAt = now,
                    CreatedByParentId = parentId
                });
                await _context.SaveChangesAsync(token);
                plainCode = code;
                return true;
            }, ct);
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            error = ParentLinkErrorCodes.Busy;
        }
        catch (ParentLinkLockTimeoutException)
        {
            error = ParentLinkErrorCodes.Busy;
        }

        switch (error)
        {
            case null:
                break;
            case ParentLinkErrorCodes.NotFound:
                return Fail<ParentInviteCodeResultDto>(error, notFound: true);
            case ParentLinkErrorCodes.NotPrimaryParent:
                return Fail<ParentInviteCodeResultDto>(error, forbidden: true);
            default:
                return Fail<ParentInviteCodeResultDto>(error, conflict: true);
        }

        _logger?.LogInformation("[ParentLinks] İkinci veli davet kodu üretildi: studentId={StudentId} parentId={ParentId} expiresAt={ExpiresAt:o}",
            studentId, parentId, expiresAt);
        return new ParentInviteCodeResultDto
        {
            Success = true,
            Code = plainCode,
            ExpiresAt = expiresAt,
            Message = _localizer["parentLinks.codeCreated"]
        };
    }

    public async Task<ParentLinkResponseDto> ApproveSecondParentAsync(int linkId, int parentUserId, CancellationToken ct = default)
    {
        var access = await ResolveSecondParentRequestAsync(linkId, parentUserId, ct);
        if (access.Failure != null)
            return access.Failure;
        var target = access.Target!;

        if (target.Status == ParentStudentLinkStatus.Active)
            return Ok(target.Id, "parentLinks.approved");

        var callerParentId = access.CallerParentId;
        return await ActivatePendingAsync(
            target.Id, target.ParentId, target.ParentUserId, target.StudentId, target.StudentUserId,
            ParentStudentLinkOrigin.InviteCode,
            authorize: family => family.Primary?.ParentId == callerParentId,
            ct);
    }

    public Task<ParentLinkResponseDto> RejectSecondParentAsync(int linkId, int parentUserId, CancellationToken ct = default)
        // Yetki (404/403) RevokeCoreAsync'te tek okumayla: ayrı ön çözüm yok (review: yinelenen yetki sorgusu).
        => RevokeCoreAsync(new RevokeRequest(linkId, parentUserId, ActorKind.Parent, PendingOnly: true, InviteCodeOnly: true,
            "parentLinks.rejected"), ct);

    private sealed record SecondParentTarget(
        int Id, ParentStudentLinkStatus Status, int ParentId, int ParentUserId, int StudentId, int StudentUserId);

    private sealed record SecondParentAccess(SecondParentTarget? Target, int CallerParentId, ParentLinkResponseDto? Failure);

    /// <summary>
    /// İkinci veli isteği üzerinde birincil veli yetkisi (okuma anı; yazım ayrıca kilit altında yeniden doğrular). Çağıranın
    /// kendi isteği / çocuğa Active bağlı olmayan çağıran → 404; bağlı ama birincil değil → 403. Aile tek sorguyla okunur.
    /// </summary>
    private async Task<SecondParentAccess> ResolveSecondParentRequestAsync(int linkId, int parentUserId, CancellationToken ct)
    {
        var callerParentId = await ParentIdOfAsync(parentUserId, ct);
        if (callerParentId == null)
            return new SecondParentAccess(null, 0, Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true));

        var target = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Id == linkId && l.Origin == ParentStudentLinkOrigin.InviteCode && !l.Parent.IsDeleted && !l.Student.IsDeleted)
            .Select(l => new SecondParentTarget(l.Id, l.Status, l.ParentId, l.Parent.UserId, l.StudentId, l.Student.UserId))
            .FirstOrDefaultAsync(ct);
        var family = target == null || target.ParentId == callerParentId
            ? []
            : await ParentLinkPrimary.ActiveRowsAsync(_context, new[] { target.StudentId }, ct);
        if (target == null || !family.Any(r => r.ParentId == callerParentId))
            return new SecondParentAccess(null, callerParentId.Value, Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true));

        if (ParentLinkPrimary.PickPrimary(family)?.ParentId != callerParentId)
            return new SecondParentAccess(null, callerParentId.Value, Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotPrimaryParent, forbidden: true));

        return new SecondParentAccess(target, callerParentId.Value, null);
    }

    /// <summary>
    /// Bekleyen isteği Active yapar (geçiş dönemi öğrenci onayı ya da birincil veli onayı). Öğrenci kilidi altında: birincil
    /// işaret onarılır, <paramref name="authorize"/> (verilmişse) birincil veliye göre yeniden doğrulanır, koşullu UPDATE yalnızca
    /// süresi dolmamış, beklenen kuruluş yolundaki Pending satırı Active yapar (eşzamanlı ret/iptal/onay tek kazanır); öğrencinin
    /// birincil velisi yoksa onaylanan veli birincil olur. ParentLinkedEvent aynı transaction'da.
    /// </summary>
    private async Task<ParentLinkResponseDto> ActivatePendingAsync(
        int linkId, int parentId, int parentUserId, int studentId, int studentUserId, ParentStudentLinkOrigin origin,
        Func<ParentLinkPrimary.FamilyState, bool>? authorize, CancellationToken ct)
    {
        // #423: bildirim hedefi (sub) + kısa adlar transaction DIŞINDA, fail-soft çözülür (auth-api yoksa boş; consumer yedeğe düşer).
        var notifyUsers = await ParentNotificationSupport.LookupAsync(
            _authApiClient, new[] { parentUserId, studentUserId }, _logger, ct);
        var parentNotify = ParentNotificationSupport.Of(notifyUsers, parentUserId);
        var studentNotify = ParentNotificationSupport.Of(notifyUsers, studentUserId);

        var approvedNow = false;
        var forbidden = false;
        try
        {
            await ParentLinkPrimary.InStudentLockAsync(_context, studentId, async token =>
            {
                approvedNow = false;
                forbidden = false;
                var now = Now;
                var cutoff = now - ParentLinkRules.PendingValidityFor(origin);

                var family = await ParentLinkPrimary.EnsurePrimaryAsync(_context, studentId, token);
                if (authorize != null && !authorize(family))
                {
                    forbidden = true;
                    return true;
                }

                var affected = await _context.ParentStudentLinks
                    .Where(l => l.Id == linkId && l.Status == ParentStudentLinkStatus.Pending && l.Origin == origin && l.CreatedAt > cutoff)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(l => l.Status, ParentStudentLinkStatus.Active)
                        .SetProperty(l => l.ActivatedAt, now), token);
                if (affected == 0)
                    return true;

                // Birincil yoksa (yalnız geçiş dönemindeki ilk onayda) onaylanan veli birincil olur.
                if (family.Primary == null)
                    await ParentLinkPrimary.EnsurePrimaryAsync(_context, studentId, token);

                AddOutbox(new ParentLinkedEvent
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
                }, now);
                await _context.SaveChangesAsync(token);
                approvedNow = true;
                return true;
            }, ct);
        }
        catch (ParentLinkLockTimeoutException)
        {
            return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.Busy, conflict: true);
        }

        if (forbidden)
            return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotPrimaryParent, forbidden: true);

        if (!approvedNow)
        {
            // Arada onaylanmış olabilir (idempotent); aksi halde süresi dolmuş/reddedilmiş/iptal → yok.
            return await StatusOfAsync(linkId, ct) == ParentStudentLinkStatus.Active
                ? Ok(linkId, "parentLinks.approved")
                : Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true);
        }

        _logger?.LogInformation("[ParentLinks] Veli bağlantısı onaylandı: linkId={LinkId} origin={Origin}", linkId, origin);
        return Ok(linkId, "parentLinks.approved");
    }

    // ---- Veli: kodu kullan / çocuklarım -----------------------------------------------------------------------------

    public async Task<RedeemParentInviteCodeResultDto> RedeemAsync(int parentUserId, string? code, CancellationToken ct = default)
    {
        var parentId = await ParentIdOfAsync(parentUserId, ct);
        if (parentId == null)
            return Fail<RedeemParentInviteCodeResultDto>(ParentLinkErrorCodes.ProfileNotFound, notFound: true);

        var guard = await _guard.CheckAsync(parentUserId, ct);
        if (!guard.Allowed)
            return RateLimited(guard.RetryAfterSeconds);

        // #419 review D1: velinin KENDİ tavanı kod aranmadan önce — sonuç kodun geçerliliğine bağlı değil.
        var lookupNow = Now;
        if (await _context.ParentStudentLinks.Where(l => l.ParentId == parentId).Where(ParentLinkPrimary.IsOpen(lookupNow)).CountAsync(ct)
            >= ParentLinkRules.MaxActiveChildrenPerParent)
            return Fail<RedeemParentInviteCodeResultDto>(ParentLinkErrorCodes.ParentLimitReached, conflict: true);

        // Boş / çok uzun / bozuk biçim: Normalize null döner → genel hata + başarısız deneme (re-review madde 8).
        var normalized = _hasher.Normalize(code);
        if (normalized == null)
            return await InvalidCodeAsync(parentUserId, ct);

        // #436: yalnız birincil velinin ürettiği ikinci veli kodu geçerli; #419 öğrenci kodları (CreatedByParentId null) genel hata.
        var hash = _hasher.Hash(normalized);
        var candidate = await _context.ParentInviteCodes.AsNoTracking()
            .Where(c => c.CodeHash == hash && c.UsedAt == null && c.ExpiresAt > lookupNow && c.CreatedByParentId != null)
            .Select(c => new { c.Id, c.StudentId, CreatedByParentId = c.CreatedByParentId!.Value })
            .FirstOrDefaultAsync(ct);
        if (candidate == null)
            return await InvalidCodeAsync(parentUserId, ct);

        string? error = null;
        ParentStudentLink? link = null;
        try
        {
            await ParentLinkPrimary.InStudentLockAsync(_context, candidate.StudentId, async token =>
            {
                error = null;
                link = null;
                var now = Now;

                // Kilit altında yeniden doğrula: aynı kodu eşzamanlı kullanan ya da arada yeni kod üreten istek olabilir.
                var invite = await _context.ParentInviteCodes
                    .FirstOrDefaultAsync(c => c.Id == candidate.Id && c.UsedAt == null && c.ExpiresAt > now && c.CreatedByParentId != null, token);
                var studentUserId = await _context.Students.AsNoTracking()
                    .Where(s => s.Id == candidate.StudentId)
                    .Select(s => (int?)s.UserId)
                    .FirstOrDefaultAsync(token);
                // Re-review (#419): kendi kendine bağlanma (veli hesabı = öğrenci hesabı) da genel hata. #436: kodu üreten
                // velinin bağlantısı bitmişse (koparıldı / ayrıldı / hesabı silindi) kod artık kimsenin adına davet değildir.
                if (invite == null || studentUserId == null || studentUserId == parentUserId
                    || !await HasLiveActiveLinkAsync(candidate.CreatedByParentId, candidate.StudentId, token))
                {
                    error = ParentLinkErrorCodes.InvalidCode;
                    return false;
                }

                // Süresi dolmuş Pending satırları kapat: çift tekil index'i (Active|Pending) yeniden isteği engellemesin.
                await _context.ParentStudentLinks
                    .Where(l => l.StudentId == candidate.StudentId)
                    .Where(ParentLinkPrimary.IsExpiredPending(now))
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                        .SetProperty(l => l.RevokedAt, now), token);

                if (await _context.ParentStudentLinks.AnyAsync(l => l.ParentId == parentId && l.StudentId == candidate.StudentId
                        && (l.Status == ParentStudentLinkStatus.Active || l.Status == ParentStudentLinkStatus.Pending), token))
                {
                    error = ParentLinkErrorCodes.AlreadyLinked;
                    return false;
                }

                // #419 review D1 + re-review madde 5: kod eşleştikten SONRA hiçbir tavan ayrı hata vermez — öğrencinin ya da
                // (kilit öncesi kontrol ile arada dolmuşsa) velinin tavanı dolu ise genel hata; kod tüketilmez, başarısızlık sayılır.
                if (await _context.ParentStudentLinks.Where(l => l.StudentId == candidate.StudentId).Where(ParentLinkPrimary.IsOpen(now)).CountAsync(token)
                        >= ParentLinkRules.MaxActiveParentsPerStudent
                    || await _context.ParentStudentLinks.Where(l => l.ParentId == parentId).Where(ParentLinkPrimary.IsOpen(now)).CountAsync(token)
                        >= ParentLinkRules.MaxActiveChildrenPerParent)
                {
                    error = ParentLinkErrorCodes.InvalidCode;
                    return false;
                }

                // #436: bağlantı BİRİNCİL VELİ onaylayana kadar Pending; kod tüketilir. Event yalnızca onayda (Active'e geçiş).
                invite.UsedAt = now;
                invite.UsedByParentId = parentId;
                link = new ParentStudentLink
                {
                    ParentId = parentId.Value,
                    StudentId = candidate.StudentId,
                    Status = ParentStudentLinkStatus.Pending,
                    Origin = ParentStudentLinkOrigin.InviteCode,
                    IsPrimary = false,
                    CreatedAt = now
                };
                _context.ParentStudentLinks.Add(link);
                await _context.SaveChangesAsync(token);
                return true;
            }, ct, parentId: parentId.Value);
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // Kilit Postgres'te çifti zaten serileştirir; filtreli tekil index son savunma hattı.
            error = ParentLinkErrorCodes.AlreadyLinked;
        }
        catch (ParentLinkLockTimeoutException)
        {
            error = ParentLinkErrorCodes.Busy;
        }

        if (error == ParentLinkErrorCodes.InvalidCode)
            return await InvalidCodeAsync(parentUserId, ct);
        if (error != null)
            return Fail<RedeemParentInviteCodeResultDto>(error, conflict: true);

        var pending = link!;
        _logger?.LogInformation("[ParentLinks] İkinci veli isteği (birincil veli onayı bekliyor): linkId={LinkId} parentId={ParentId} studentId={StudentId}",
            pending.Id, parentId, candidate.StudentId);

        return new RedeemParentInviteCodeResultDto
        {
            Success = true,
            ObjectId = pending.Id,
            Message = _localizer["parentLinks.requested"],
            Child = PendingChild(pending.Id, pending.CreatedAt, pending.Origin)
        };
    }

    public async Task<IReadOnlyList<LinkedChildDto>?> GetParentChildrenAsync(int parentUserId, CancellationToken ct = default)
    {
        var parentId = await ParentIdOfAsync(parentUserId, ct);
        if (parentId == null)
            return null;

        var now = Now;
        var rows = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == parentId)
            .Where(ParentLinkPrimary.IsOpen(now))
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Take(ParentLinkRules.MaxActiveChildrenPerParent)
            .Select(l => new
            {
                l.Id,
                l.Status,
                l.Origin,
                l.StudentId,
                StudentUserId = l.Student.UserId,
                GradeName = l.Student.Grade != null ? l.Student.Grade.Name : null,
                // #361: yalnızca DOĞRULANMIŞ okul gösterilir — öğrencinin kendi seçtiği (bekleyen) okul ya da legacy serbest
                // metin SchoolName veliye "okulu" diye sunulmaz.
                SchoolName = l.Student.SchoolVerifiedAt != null && l.Student.School != null ? l.Student.School.Name : null,
                l.ActivatedAt,
                l.CreatedAt
            })
            .ToListAsync(ct);

        // Ad çözümü ve diğer veliler yalnızca Active çocuklar için — Pending'de öğrenci verisi dönmez (auth-api'ye id de gitmez).
        var activeRows = rows.Where(r => r.Status == ParentStudentLinkStatus.Active).ToList();
        var activeStudentIds = activeRows.Select(r => r.StudentId).Distinct().ToList();
        var family = activeStudentIds.Count == 0
            ? []
            : await ParentLinkPrimary.ActiveRowsAsync(_context, activeStudentIds, ct);

        // #436: birincil veli kuralı okuma anında (silinen birincilin yerine en eski Active) — yazmaz.
        var primaryStudentIds = family
            .GroupBy(f => f.StudentId)
            .Where(g => ParentLinkPrimary.PickPrimary(g)?.ParentId == parentId)
            .Select(g => g.Key)
            .ToList();

        var requests = primaryStudentIds.Count == 0
            ? []
            : await _context.ParentStudentLinks.AsNoTracking()
                .Where(l => primaryStudentIds.Contains(l.StudentId) && l.Origin == ParentStudentLinkOrigin.InviteCode && !l.Parent.IsDeleted)
                .Where(ParentLinkPrimary.IsLivePending(now))
                .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
                .Select(l => new RequestRow(l.Id, l.StudentId, l.Parent.UserId, l.CreatedAt))
                .ToListAsync(ct);

        // Tavan sayımı sunucunun kuralıyla aynı (IsOpen: geçiş dönemindeki eski istekler dahil).
        var openCounts = primaryStudentIds.Count == 0
            ? new Dictionary<int, int>()
            : await _context.ParentStudentLinks.AsNoTracking()
                .Where(l => primaryStudentIds.Contains(l.StudentId))
                .Where(ParentLinkPrimary.IsOpen(now))
                .GroupBy(l => l.StudentId)
                .Select(g => new { StudentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.StudentId, g => g.Count, ct);

        var codes = primaryStudentIds.Count == 0
            ? []
            : await _context.ParentInviteCodes.AsNoTracking()
                .Where(c => primaryStudentIds.Contains(c.StudentId) && c.UsedAt == null && c.ExpiresAt > now && c.CreatedByParentId != null)
                .Select(c => new CodeRow(c.StudentId, c.ExpiresAt))
                .ToListAsync(ct);

        var coParents = family.Where(f => primaryStudentIds.Contains(f.StudentId) && f.ParentId != parentId).ToList();
        var users = await LookupUsersAsync(
            activeRows.Select(r => r.StudentUserId)
                .Concat(coParents.Select(f => f.ParentUserId))
                .Concat(requests.Select(r => r.ParentUserId)), ct);

        return rows.Select(r =>
        {
            if (r.Status != ParentStudentLinkStatus.Active)
                return PendingChild(r.Id, r.CreatedAt, r.Origin);

            var isPrimary = primaryStudentIds.Contains(r.StudentId);
            var dto = new LinkedChildDto
            {
                LinkId = r.Id,
                Status = nameof(ParentStudentLinkStatus.Active),
                StudentId = r.StudentId, // issue #420: veli paneli anahtarı (yalnız Active)
                StudentName = NameOf(users, r.StudentUserId, "parentLinks.fallbackStudentName"),
                GradeName = r.GradeName,
                SchoolName = r.SchoolName,
                LinkedAt = AsUtc(r.ActivatedAt ?? r.CreatedAt),
                RequestedAt = AsUtc(r.CreatedAt),
                IsPrimary = isPrimary,
                MaxParents = ParentLinkRules.MaxActiveParentsPerStudent
            };
            if (!isPrimary)
                return dto;

            dto.CoParents = coParents.Where(f => f.StudentId == r.StudentId)
                .OrderBy(f => f.ActivatedOrCreatedAt).ThenBy(f => f.LinkId)
                .Select(f => new CoParentDto
                {
                    LinkId = f.LinkId,
                    Status = nameof(ParentStudentLinkStatus.Active),
                    ParentName = NameOf(users, f.ParentUserId, "parentLinks.fallbackParentName"),
                    LinkedAt = AsUtc(f.ActivatedOrCreatedAt),
                    RequestedAt = AsUtc(f.ActivatedOrCreatedAt)
                })
                .Concat(requests.Where(q => q.StudentId == r.StudentId).Select(q => new CoParentDto
                {
                    LinkId = q.Id,
                    Status = nameof(ParentStudentLinkStatus.Pending),
                    ParentName = NameOf(users, q.ParentUserId, "parentLinks.fallbackParentName"),
                    // Birincil veli kimi onayladığını bilsin — yalnızca maskeli e-posta.
                    ParentEmailMasked = MaskedEmailOf(users, q.ParentUserId),
                    RequestedAt = AsUtc(q.CreatedAt),
                    PendingExpiresAt = AsUtc(q.CreatedAt + ParentLinkRules.PendingValidity)
                }))
                .ToList();
            dto.OpenParents = openCounts.GetValueOrDefault(r.StudentId, 1);
            var codeExpiry = codes.Where(c => c.StudentId == r.StudentId).Select(c => (DateTime?)c.ExpiresAt).Max();
            dto.SecondParentCodeExpiresAt = codeExpiry is DateTime e ? AsUtc(e) : null;
            return dto;
        }).ToList();
    }

    private sealed record RequestRow(int Id, int StudentId, int ParentUserId, DateTime CreatedAt);

    private sealed record CodeRow(int StudentId, DateTime ExpiresAt);

    private static LinkedChildDto PendingChild(int linkId, DateTime createdAt, ParentStudentLinkOrigin origin) => new()
    {
        LinkId = linkId,
        Status = nameof(ParentStudentLinkStatus.Pending),
        RequestedAt = AsUtc(createdAt),
        PendingExpiresAt = AsUtc(createdAt + ParentLinkRules.PendingValidityFor(origin)),
        MaxParents = ParentLinkRules.MaxActiveParentsPerStudent
    };

    // ---- Koparma ----------------------------------------------------------------------------------------------------

    public Task<ParentLinkResponseDto> RevokeAsync(int linkId, int parentUserId, CancellationToken ct = default)
        => RevokeCoreAsync(new RevokeRequest(linkId, parentUserId, ActorKind.Parent, PendingOnly: false, InviteCodeOnly: false,
            "parentLinks.revoked"), ct);

    public Task<ParentLinkResponseDto> AdminRevokeAsync(int linkId, int adminUserId, string adminKeycloakId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminKeycloakId);
        return RevokeCoreAsync(new RevokeRequest(linkId, adminUserId, ActorKind.Admin, PendingOnly: false, InviteCodeOnly: false,
            "parentLinks.revoked", adminKeycloakId), ct);
    }

    /// <summary>
    /// Velinin koparma kuralı — okuma anındaki ön kontrol (auth-api çağrısından önce) ve kilit altındaki kesin kontrol aynı
    /// fonksiyonu kullanır. Bekleyen istek: sahibi iptal eder, birincil veli reddeder (<paramref name="inviteCodeOnly"/>: yalnız
    /// birincil). Active: birincil başkasınınkini koparır; her veli kendi bağlantısından ayrılır — ama öğrencinin TEK Active
    /// velisi ayrılamaz (her öğrencinin velisi olmalı).
    /// </summary>
    private static RevokeDecision DecideParentRevoke(
        int callerParentId, int targetParentId, ParentStudentLinkStatus status, bool inviteCodeOnly,
        IReadOnlyCollection<ParentLinkPrimary.ActiveLinkRow> active)
    {
        var isPrimary = ParentLinkPrimary.PickPrimary(active)?.ParentId == callerParentId;
        var isOwn = targetParentId == callerParentId;
        if (status == ParentStudentLinkStatus.Pending)
            return (inviteCodeOnly ? isPrimary : isOwn || isPrimary) ? RevokeDecision.Allow : RevokeDecision.Forbidden;
        if (!isOwn)
            return isPrimary ? RevokeDecision.Allow : RevokeDecision.Forbidden;
        return active.Any(r => r.ParentId != callerParentId) ? RevokeDecision.Allow : RevokeDecision.LastParent;
    }

    private ParentLinkResponseDto Denied(RevokeDecision decision) => decision == RevokeDecision.LastParent
        ? Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.LastParentCannotLeave, conflict: true)
        : Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotPrimaryParent, forbidden: true);

    /// <summary>
    /// Koparma ve retin TEK gövdesi. Görünürlük (404) ve yetki ön kontrolü transaction dışında, aile tek sorguyla okunarak (auth-api
    /// ad çözümünden ÖNCE — yetkisiz istek dış çağrı tetiklemez); yetki öğrenci kilidi altında yeniden doğrulanır. Koşullu UPDATE
    /// (<c>Status == okunan durum</c>) başarısızsa durum yeniden okunur (#419 re-review A — eşzamanlı onay yarışı): Revoked →
    /// idempotent başarı; Pending iken Active olduysa karar Active kuralıyla yeniden verilir, ret ise "artık bekleyen istek yok" →
    /// NotFound. Active bağlantı koparılınca birincillik kilit altında devredilir (kalan en eski Active veli).
    /// </summary>
    private async Task<ParentLinkResponseDto> RevokeCoreAsync(RevokeRequest request, CancellationToken ct)
    {
        var target = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Id == request.LinkId)
            .Select(l => new
            {
                l.Id, l.Status, l.Origin, l.ParentId, l.StudentId, l.CreatedAt,
                ParentUserId = l.Parent.UserId,
                StudentUserId = l.Student.UserId,
                PartyDeleted = l.Parent.IsDeleted || l.Student.IsDeleted
            })
            .FirstOrDefaultAsync(ct);
        if (target == null)
        {
            if (request.Actor == ActorKind.Admin)
                await AuditAdminRevokeAsync(request, AdminUserActionOutcome.NotFound);
            return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true);
        }

        // ---- Görünürlük: bağlantının tarafı olmayana varlığı sızdırmadan 404.
        int callerParentId = 0;
        IReadOnlyList<ParentLinkPrimary.ActiveLinkRow> family = [];
        switch (request.Actor)
        {
            case ActorKind.Student:
                // Öğrenci yalnız geçiş dönemindeki kendi #419 isteğini reddedebilir (koparma yok).
                if (target.StudentUserId != request.UserId || target.Origin != ParentStudentLinkOrigin.LegacyV1)
                    return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true);
                break;
            case ActorKind.Parent:
                var parentId = await ParentIdOfAsync(request.UserId, ct);
                if (parentId == null)
                    return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true);
                callerParentId = parentId.Value;
                family = await ParentLinkPrimary.ActiveRowsAsync(_context, new[] { target.StudentId }, ct);
                // Ret (InviteCodeOnly): yalnız başka velinin, silinmemiş taraflı ikinci veli isteği; kendi isteği revoke ile iptal edilir.
                if ((target.ParentId != callerParentId && !family.Any(r => r.ParentId == callerParentId))
                    || (request.InviteCodeOnly && (target.Origin != ParentStudentLinkOrigin.InviteCode
                                                   || target.ParentId == callerParentId || target.PartyDeleted)))
                    return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true);
                break;
            case ActorKind.Admin:
                break;
        }

        var isOwn = request.Actor == ActorKind.Parent && target.ParentId == callerParentId;
        var status = target.Status;
        for (var attempt = 0; attempt < MaxRevokeAttempts; attempt++)
        {
            if (status == ParentStudentLinkStatus.Revoked)
            {
                // İdempotent başarı yalnız işlemi zaten yapabilecek olana (kendi bağlantısı / birincil veli / admin / öğrencinin
                // kendi reddi); bağlı ama birincil olmayan veli başkasının geçmiş satırında da 403 alır.
                if (request.Actor == ActorKind.Parent && !isOwn && ParentLinkPrimary.PickPrimary(family)?.ParentId != callerParentId)
                    return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotPrimaryParent, forbidden: true);
                if (request.Actor == ActorKind.Admin)
                    await AuditAdminRevokeAsync(request, AdminUserActionOutcome.NoChange);
                return Ok(target.Id, request.OkMessageKey);
            }
            if (request.PendingOnly && status != ParentStudentLinkStatus.Pending)
                return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true); // aktif bağlantı: revoke
            if (request.Actor == ActorKind.Student
                && target.CreatedAt <= Now - ParentLinkRules.LegacyPendingValidity)
                return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true); // geçiş süresi bitti

            // Ucuz ön kontrol (security MINOR-3): yetkisiz istek auth-api ad çözümüne hiç gitmez.
            if (request.Actor == ActorKind.Parent)
            {
                var pre = DecideParentRevoke(callerParentId, target.ParentId, status, request.InviteCodeOnly, family);
                if (pre != RevokeDecision.Allow)
                    return Denied(pre);
            }

            var wasActive = status == ParentStudentLinkStatus.Active;
            var revokedByRole = request.Actor switch
            {
                ActorKind.Student => RevokedByRoles.Student,
                ActorKind.Admin => RevokedByRoles.Admin,
                _ => isOwn ? RevokedByRoles.Parent : RevokedByRoles.PrimaryParent
            };
            // Veli kendi Active bağlantısından ayrılırsa (kalan) birincil veli de haberdar edilir.
            var primaryAfter = wasActive && revokedByRole == RevokedByRoles.Parent
                ? ParentLinkPrimary.PickPrimary(family.Where(r => r.LinkId != target.Id))
                : null;

            // #423: yalnız aktif bağlantı koparılınca event yazılır; hedef/ad çözümü transaction dışında, fail-soft.
            var notifyUsers = wasActive
                ? await ParentNotificationSupport.LookupAsync(_authApiClient,
                    new[] { target.ParentUserId, target.StudentUserId, primaryAfter?.ParentUserId ?? 0 }.Where(id => id > 0).ToArray(),
                    _logger, ct)
                : new Dictionary<int, NotificationUser>();
            var parentNotify = ParentNotificationSupport.Of(notifyUsers, target.ParentUserId);
            var studentNotify = ParentNotificationSupport.Of(notifyUsers, target.StudentUserId);
            var primaryNotify = ParentNotificationSupport.Of(notifyUsers, primaryAfter?.ParentUserId ?? 0);
            var expected = status;
            var revokedNow = false;
            RevokeDecision? denial = null;
            try
            {
                await ParentLinkPrimary.InStudentLockAsync(_context, target.StudentId, async token =>
                {
                    revokedNow = false;
                    denial = null;
                    var now = Now;

                    var familyNow = await ParentLinkPrimary.EnsurePrimaryAsync(_context, target.StudentId, token);
                    if (request.Actor == ActorKind.Parent)
                    {
                        var decision = DecideParentRevoke(callerParentId, target.ParentId, expected, request.InviteCodeOnly, familyNow.Active);
                        if (decision != RevokeDecision.Allow)
                        {
                            denial = decision;
                            return true; // birincil onarımı kalsın
                        }
                    }

                    var affected = await _context.ParentStudentLinks
                        .Where(l => l.Id == target.Id && l.Status == expected)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                            .SetProperty(l => l.IsPrimary, false)
                            .SetProperty(l => l.RevokedAt, now)
                            .SetProperty(l => l.RevokedByUserId, request.UserId), token);
                    if (affected == 0)
                        return true;

                    if (wasActive)
                    {
                        // Birincil koparıldıysa kalan en eski Active veli birincil olur (değilse no-op).
                        var after = await ParentLinkPrimary.EnsurePrimaryAsync(_context, target.StudentId, token);
                        var notifyPrimary = revokedByRole == RevokedByRoles.Parent && after.Primary != null;

                        // Unlinked event'i yalnızca gerçekten AKTİF olan bağlantı için (Pending iptali/reddi bildirim değil).
                        AddOutbox(new ParentUnlinkedEvent
                        {
                            EventId = Guid.NewGuid(),
                            LinkId = target.Id,
                            ParentId = target.ParentId,
                            ParentUserId = target.ParentUserId,
                            StudentId = target.StudentId,
                            StudentUserId = target.StudentUserId,
                            RevokedByRole = revokedByRole,
                            RevokedByUserId = request.UserId,
                            ParentKeycloakId = parentNotify.KeycloakId,
                            StudentKeycloakId = studentNotify.KeycloakId,
                            ParentDisplayName = parentNotify.DisplayName,
                            StudentDisplayName = studentNotify.DisplayName,
                            RevokedAtUtc = now,
                            PrimaryParentUserId = notifyPrimary ? after.Primary!.ParentUserId : 0,
                            PrimaryParentKeycloakId = notifyPrimary && after.Primary!.ParentUserId == primaryAfter?.ParentUserId
                                ? primaryNotify.KeycloakId
                                : string.Empty
                        }, now);
                    }

                    if (request.Actor == ActorKind.Admin)
                        AddAdminAudit(request, AdminUserActionOutcome.Succeeded, now);

                    await _context.SaveChangesAsync(token);
                    revokedNow = true;
                    return true;
                }, ct);
            }
            catch (ParentLinkLockTimeoutException)
            {
                return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.Busy, conflict: true);
            }

            if (denial is RevokeDecision denied)
                return Denied(denied);

            if (revokedNow)
            {
                _logger?.LogInformation("[ParentLinks] Bağlantı koparıldı: linkId={LinkId} by={Role} wasActive={WasActive}",
                    target.Id, revokedByRole, wasActive);
                return Ok(target.Id, request.OkMessageKey);
            }

            // Yarış: başka istek durumu değiştirdi (onay / ret / koparma) — güncel durumla yeniden karar ver.
            status = await StatusOfAsync(target.Id, ct) ?? ParentStudentLinkStatus.Revoked;
            if (request.Actor == ActorKind.Parent)
                family = await ParentLinkPrimary.ActiveRowsAsync(_context, new[] { target.StudentId }, ct);
        }

        return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.Busy, conflict: true);
    }

    // ---- yardımcılar ------------------------------------------------------------------------------------------------

    private void AddAdminAudit(RevokeRequest request, AdminUserActionOutcome outcome, DateTime now)
        => _context.AdminUserActionLogs.Add(new AdminUserActionLog
        {
            ActorKeycloakId = request.AdminKeycloakId!.Trim(),
            Action = AdminUserAction.ParentLinkRevoked,
            TargetType = AdminUserTargetType.ParentLink,
            TargetId = request.LinkId,
            Outcome = outcome,
            OccurredAtUtc = now
        });

    /// <summary>Admin müdahalesinin yan etkisiz sonuçları (bulunamadı / zaten koparılmış) da denetim izine (security MINOR-3).</summary>
    private async Task AuditAdminRevokeAsync(RevokeRequest request, AdminUserActionOutcome outcome)
    {
        _context.ChangeTracker.Clear();
        AddAdminAudit(request, outcome, Now);
        // İstemci bağlantıyı kesse bile iz tamamlansın (AdminUserActionAuditService deseni).
        await _context.SaveChangesAsync(CancellationToken.None);
        _context.ChangeTracker.Clear();
    }

    private Task<int?> StudentIdOfAsync(int studentUserId, CancellationToken ct)
        => _context.Students.AsNoTracking()
            .Where(s => s.UserId == studentUserId)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);

    private Task<int?> ParentIdOfAsync(int parentUserId, CancellationToken ct)
        => _context.Parents.AsNoTracking()
            .Where(p => p.UserId == parentUserId)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>Velinin bu öğrenciye velisi silinmemiş Active bağlantısı var mı.</summary>
    private Task<bool> HasLiveActiveLinkAsync(int parentId, int studentId, CancellationToken ct)
        => _context.ParentStudentLinks
            .Where(l => l.ParentId == parentId && l.StudentId == studentId)
            .Where(ParentLinkPrimary.IsLiveActive)
            .AnyAsync(ct);

    private Task<ParentStudentLinkStatus?> StatusOfAsync(int linkId, CancellationToken ct)
        => _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Id == linkId)
            .Select(l => (ParentStudentLinkStatus?)l.Status)
            .FirstOrDefaultAsync(ct);

    /// <summary>Outbox satırı — çağıranın transaction'ında, aynı SaveChanges ile yazılır.</summary>
    private void AddOutbox<TEvent>(TEvent payload, DateTime now) where TEvent : class
        => _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<TEvent>(),
            Content = JsonSerializer.Serialize(payload),
            CreatedAt = now
        });

    private async Task<RedeemParentInviteCodeResultDto> InvalidCodeAsync(int parentUserId, CancellationToken ct)
    {
        // Hangi kısmın tuttuğu (yok / süresi dolmuş / kullanılmış / biçim / tavan / kendine bağlanma / eski öğrenci kodu /
        // kodu üreten velinin bağlantısı bitmiş) ne yanıtta ne logda ayrışır.
        _logger?.LogInformation("[ParentLinks] Geçersiz davet kodu denemesi: parentUserId={ParentUserId}", parentUserId);
        await _guard.RecordFailureAsync(parentUserId, ct);
        return Fail<RedeemParentInviteCodeResultDto>(ParentLinkErrorCodes.InvalidCode);
    }

    private RedeemParentInviteCodeResultDto RateLimited(int retryAfterSeconds)
    {
        var dto = Fail<RedeemParentInviteCodeResultDto>(ParentLinkErrorCodes.RateLimited);
        dto.RateLimited = true;
        dto.RetryAfterSeconds = retryAfterSeconds;
        dto.Message = _localizer["parentLinks.redeemRateLimited"];
        return dto;
    }

    private ParentLinkResponseDto Ok(int linkId, string messageKey)
        => new() { Success = true, ObjectId = linkId, Message = _localizer[messageKey] };

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private string NameOf(IReadOnlyDictionary<int, UserLookupResultDto>? users, int userId, string fallbackKey)
        => users != null && users.TryGetValue(userId, out var u) && !string.IsNullOrWhiteSpace(u.FullName)
            ? u.FullName
            : _localizer[fallbackKey];

    private static string MaskedEmailOf(IReadOnlyDictionary<int, UserLookupResultDto>? users, int userId)
        => users != null && users.TryGetValue(userId, out var u) ? EmailMask.ApplyWithDomain(u.Email) : string.Empty;

    /// <summary>Ad zenginleştirmesi: auth-api erişilemezse ya da süre aşılırsa null (yerelleştirilmiş yedek ad).</summary>
    private async Task<IReadOnlyDictionary<int, UserLookupResultDto>?> LookupUsersAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, UserLookupResultDto>();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(NameLookupTimeout);
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(ids, timeout.Token);
            return users.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _logger?.LogWarning(ex, "[ParentLinks] Kullanıcı adları çözülemedi ({Count} kullanıcı).", ids.Count);
            return null;
        }
    }

    private T Fail<T>(string code, bool notFound = false, bool conflict = false, bool forbidden = false)
        where T : ParentLinkResponseDto, new() => new()
    {
        Success = false,
        NotFound = notFound,
        Conflict = conflict,
        Forbidden = forbidden,
        ErrorCode = code,
        Message = _localizer["parentLinks.errors." + char.ToLowerInvariant(code[0]) + code[1..]]
    };
}
