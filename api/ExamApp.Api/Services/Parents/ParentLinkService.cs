using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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

    /// <summary>
    /// Açık bağlantı: Active ya da süresi dolmamış Pending; silinmiş (soft-delete) veli/öğrenci tavan tüketmez (review madde 8).
    /// </summary>
    private static Expression<Func<ParentStudentLink, bool>> IsOpen(DateTime pendingCutoff) =>
        l => (l.Status == ParentStudentLinkStatus.Active
              || (l.Status == ParentStudentLinkStatus.Pending && l.CreatedAt > pendingCutoff))
             && !l.Parent.IsDeleted && !l.Student.IsDeleted;

    private static DateTime PendingCutoff(DateTime now) => now - ParentLinkRules.PendingValidity;

    // ---- Öğrenci: davet kodu ----------------------------------------------------------------------------------------

    public async Task<ParentInviteCodeResultDto> CreateInviteCodeAsync(int studentUserId, CancellationToken ct = default)
    {
        var studentId = await StudentIdOfAsync(studentUserId, ct);
        if (studentId == null)
            return Fail<ParentInviteCodeResultDto>(ParentLinkErrorCodes.ProfileNotFound, notFound: true);

        string? plainCode = null;
        DateTime expiresAt = default;
        string? error = null;

        try
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                plainCode = null;
                error = null;
                var now = Now;

                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                await _context.Database.AcquireStudentParentLinkLockAsync(studentId.Value, ct);

                var openParents = await _context.ParentStudentLinks
                    .Where(l => l.StudentId == studentId)
                    .Where(IsOpen(PendingCutoff(now)))
                    .CountAsync(ct);
                if (openParents >= ParentLinkRules.MaxActiveParentsPerStudent)
                {
                    error = ParentLinkErrorCodes.StudentLimitReached;
                    return;
                }

                // Öğrenci başına tek geçerli kod: öncekiler "şimdi" bitmiş sayılır (satır kalır, kullanılamaz).
                await _context.ParentInviteCodes
                    .Where(c => c.StudentId == studentId && c.UsedAt == null && c.ExpiresAt > now)
                    .ExecuteUpdateAsync(set => set.SetProperty(c => c.ExpiresAt, now), ct);

                // Kullanılmamış (süresi dolmuş olsa da) kodlar arasında hash tekil (filtreli unique index). Çakışma
                // denemeleri tükenirse satır YAZILMAZ — yeniden denenebilir hata döner (review D2).
                string? code = null;
                string? hash = null;
                for (var attempt = 0; attempt < MaxGenerateAttempts; attempt++)
                {
                    var candidate = _hasher.Generate();
                    var candidateHash = _hasher.Hash(candidate);
                    if (await _context.ParentInviteCodes.AnyAsync(c => c.CodeHash == candidateHash && c.UsedAt == null, ct))
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
                    return;
                }

                expiresAt = now.Add(ParentLinkRules.CodeValidity);
                _context.ParentInviteCodes.Add(new ParentInviteCode
                {
                    StudentId = studentId.Value,
                    CodeHash = hash,
                    ExpiresAt = expiresAt,
                    CreatedAt = now
                });
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                plainCode = code;
            });
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            error = ParentLinkErrorCodes.Busy;
        }
        catch (ParentLinkLockTimeoutException)
        {
            error = ParentLinkErrorCodes.Busy;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }

        if (error != null)
            return Fail<ParentInviteCodeResultDto>(error, conflict: true);

        _logger?.LogInformation("[ParentLinks] Davet kodu üretildi: studentId={StudentId} expiresAt={ExpiresAt:o}", studentId, expiresAt);
        return new ParentInviteCodeResultDto
        {
            Success = true,
            Code = plainCode,
            ExpiresAt = expiresAt,
            Message = _localizer["parentLinks.codeCreated"]
        };
    }

    public async Task<StudentParentLinksDto?> GetStudentParentsAsync(int studentUserId, CancellationToken ct = default)
    {
        var studentId = await StudentIdOfAsync(studentUserId, ct);
        if (studentId == null)
            return null;

        var now = Now;
        var rows = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.StudentId == studentId)
            .Where(IsOpen(PendingCutoff(now)))
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Take(ParentLinkRules.MaxActiveParentsPerStudent)
            .Select(l => new { l.Id, l.Status, ParentUserId = l.Parent.UserId, l.ActivatedAt, l.CreatedAt })
            .ToListAsync(ct);

        var activeInviteExpiresAt = await _context.ParentInviteCodes.AsNoTracking()
            .Where(c => c.StudentId == studentId && c.UsedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.ExpiresAt)
            .Select(c => (DateTime?)c.ExpiresAt)
            .FirstOrDefaultAsync(ct);

        var users = await LookupUsersAsync(rows.Select(r => r.ParentUserId), ct);
        return new StudentParentLinksDto
        {
            Items = rows.Where(r => r.Status == ParentStudentLinkStatus.Active).Select(r => new LinkedParentDto
            {
                LinkId = r.Id,
                ParentName = NameOf(users, r.ParentUserId, "parentLinks.fallbackParentName"),
                LinkedAt = AsUtc(r.ActivatedAt ?? r.CreatedAt)
            }).ToList(),
            PendingRequests = rows.Where(r => r.Status == ParentStudentLinkStatus.Pending).Select(r => new PendingParentRequestDto
            {
                LinkId = r.Id,
                ParentName = NameOf(users, r.ParentUserId, "parentLinks.fallbackParentName"),
                // Re-review: öğrenci isteği tanıyabilsin — yalnızca maskeli e-posta (a***@g***.com).
                ParentEmailMasked = users != null && users.TryGetValue(r.ParentUserId, out var u)
                    ? EmailMask.ApplyWithDomain(u.Email)
                    : string.Empty,
                RequestedAt = AsUtc(r.CreatedAt),
                ExpiresAt = AsUtc(r.CreatedAt + ParentLinkRules.PendingValidity)
            }).ToList(),
            ActiveInviteExpiresAt = activeInviteExpiresAt is DateTime e ? AsUtc(e) : null,
            MaxActiveParents = ParentLinkRules.MaxActiveParentsPerStudent
        };
    }

    // ---- Öğrenci: onay / ret ----------------------------------------------------------------------------------------

    public async Task<ParentLinkResponseDto> ApproveAsync(int linkId, int studentUserId, CancellationToken ct = default)
    {
        var target = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Id == linkId && l.Student.UserId == studentUserId && !l.Parent.IsDeleted)
            .Select(l => new { l.Id, l.Status, l.ParentId, l.StudentId, ParentUserId = l.Parent.UserId })
            .FirstOrDefaultAsync(ct);
        if (target == null)
            return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true);

        if (target.Status == ParentStudentLinkStatus.Active)
            return Ok(target.Id, "parentLinks.approved");

        var approvedNow = false;
        try
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                approvedNow = false;
                var now = Now;
                var cutoff = PendingCutoff(now);

                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                await _context.Database.AcquireStudentParentLinkLockAsync(target.StudentId, ct);

                // Koşullu UPDATE: yalnızca süresi dolmamış Pending satır Active olur; eşzamanlı ret/iptal/onay tek kazanır.
                var affected = await _context.ParentStudentLinks
                    .Where(l => l.Id == target.Id && l.Status == ParentStudentLinkStatus.Pending && l.CreatedAt > cutoff)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(l => l.Status, ParentStudentLinkStatus.Active)
                        .SetProperty(l => l.ActivatedAt, now), ct);
                if (affected == 0)
                {
                    await tx.CommitAsync(ct);
                    return;
                }

                AddOutbox(new ParentLinkedEvent
                {
                    EventId = Guid.NewGuid(),
                    LinkId = target.Id,
                    ParentId = target.ParentId,
                    ParentUserId = target.ParentUserId,
                    StudentId = target.StudentId,
                    StudentUserId = studentUserId,
                    LinkedAtUtc = now
                }, now);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                approvedNow = true;
            });
        }
        catch (ParentLinkLockTimeoutException)
        {
            return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.Busy, conflict: true);
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }

        if (!approvedNow)
        {
            // Arada onaylanmış olabilir (idempotent); aksi halde süresi dolmuş/reddedilmiş/iptal → yok.
            return await StatusOfAsync(target.Id, ct) == ParentStudentLinkStatus.Active
                ? Ok(target.Id, "parentLinks.approved")
                : Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true);
        }

        _logger?.LogInformation("[ParentLinks] Veli bağlantısı onaylandı: linkId={LinkId}", target.Id);
        return Ok(target.Id, "parentLinks.approved");
    }

    /// <summary>Ret = yalnızca öğrencinin, yalnızca Pending satıra uygulanan koparma (tek UPDATE gövdesi, <see cref="RevokeCoreAsync"/>).</summary>
    public Task<ParentLinkResponseDto> RejectAsync(int linkId, int studentUserId, CancellationToken ct = default)
        => RevokeCoreAsync(linkId, studentUserId, studentSideOnly: true, pendingOnly: true, "parentLinks.rejected", ct);

    // ---- Veli: kodu kullan / çocuklarım -----------------------------------------------------------------------------

    public async Task<RedeemParentInviteCodeResultDto> RedeemAsync(int parentUserId, string? code, CancellationToken ct = default)
    {
        var parentId = await ParentIdOfAsync(parentUserId, ct);
        if (parentId == null)
            return Fail<RedeemParentInviteCodeResultDto>(ParentLinkErrorCodes.ProfileNotFound, notFound: true);

        var guard = await _guard.CheckAsync(parentUserId, ct);
        if (!guard.Allowed)
            return RateLimited(guard.RetryAfterSeconds);

        // Review D1: velinin KENDİ tavanı kod aranmadan önce — sonuç kodun geçerliliğine bağlı değil.
        var lookupNow = Now;
        if (await _context.ParentStudentLinks.Where(l => l.ParentId == parentId).Where(IsOpen(PendingCutoff(lookupNow))).CountAsync(ct)
            >= ParentLinkRules.MaxActiveChildrenPerParent)
            return Fail<RedeemParentInviteCodeResultDto>(ParentLinkErrorCodes.ParentLimitReached, conflict: true);

        // Boş / çok uzun / bozuk biçim: Normalize null döner → genel hata + başarısız deneme (re-review madde 8).
        var normalized = _hasher.Normalize(code);
        if (normalized == null)
            return await InvalidCodeAsync(parentUserId, ct);

        var hash = _hasher.Hash(normalized);
        var candidate = await _context.ParentInviteCodes.AsNoTracking()
            .Where(c => c.CodeHash == hash && c.UsedAt == null && c.ExpiresAt > lookupNow)
            .Select(c => new { c.Id, c.StudentId })
            .FirstOrDefaultAsync(ct);
        if (candidate == null)
            return await InvalidCodeAsync(parentUserId, ct);

        string? error = null;
        ParentStudentLink? link = null;
        try
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                error = null;
                link = null;
                var now = Now;
                var cutoff = PendingCutoff(now);

                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                await _context.Database.AcquireStudentParentLinkLockAsync(candidate.StudentId, ct);
                await _context.Database.AcquireParentChildLinkLockAsync(parentId.Value, ct);

                // Kilit altında yeniden doğrula: aynı kodu eşzamanlı kullanan ya da arada yeni kod üreten istek olabilir.
                var invite = await _context.ParentInviteCodes
                    .FirstOrDefaultAsync(c => c.Id == candidate.Id && c.UsedAt == null && c.ExpiresAt > now, ct);
                var studentUserId = await _context.Students.AsNoTracking()
                    .Where(s => s.Id == candidate.StudentId)
                    .Select(s => (int?)s.UserId)
                    .FirstOrDefaultAsync(ct);
                // Re-review: kendi kendine bağlanma (veli hesabı = öğrenci hesabı) da genel hata.
                if (invite == null || studentUserId == null || studentUserId == parentUserId)
                {
                    error = ParentLinkErrorCodes.InvalidCode;
                    return;
                }

                // Süresi dolmuş Pending satırları kapat: çift tekil index'i (Active|Pending) yeniden isteği engellemesin.
                await _context.ParentStudentLinks
                    .Where(l => l.StudentId == candidate.StudentId && l.Status == ParentStudentLinkStatus.Pending && l.CreatedAt <= cutoff)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                        .SetProperty(l => l.RevokedAt, now), ct);

                if (await _context.ParentStudentLinks.AnyAsync(l => l.ParentId == parentId && l.StudentId == candidate.StudentId
                        && (l.Status == ParentStudentLinkStatus.Active || l.Status == ParentStudentLinkStatus.Pending), ct))
                {
                    error = ParentLinkErrorCodes.AlreadyLinked;
                    return;
                }

                // Review D1 + re-review madde 5: kod eşleştikten SONRA hiçbir tavan ayrı hata vermez — öğrencinin ya da (kilit
                // öncesi kontrol ile arada dolmuşsa) velinin tavanı dolu ise genel hata; kod tüketilmez, başarısızlık sayılır.
                if (await _context.ParentStudentLinks.Where(l => l.StudentId == candidate.StudentId).Where(IsOpen(cutoff)).CountAsync(ct)
                        >= ParentLinkRules.MaxActiveParentsPerStudent
                    || await _context.ParentStudentLinks.Where(l => l.ParentId == parentId).Where(IsOpen(cutoff)).CountAsync(ct)
                        >= ParentLinkRules.MaxActiveChildrenPerParent)
                {
                    error = ParentLinkErrorCodes.InvalidCode;
                    return;
                }

                // Review madde 1: bağlantı öğrenci onaylayana kadar Pending; kod tüketilir. Event yalnızca onayda.
                invite.UsedAt = now;
                invite.UsedByParentId = parentId;
                link = new ParentStudentLink
                {
                    ParentId = parentId.Value,
                    StudentId = candidate.StudentId,
                    Status = ParentStudentLinkStatus.Pending,
                    CreatedAt = now
                };
                _context.ParentStudentLinks.Add(link);
                await _context.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
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
        finally
        {
            _context.ChangeTracker.Clear();
        }

        if (error == ParentLinkErrorCodes.InvalidCode)
            return await InvalidCodeAsync(parentUserId, ct);
        if (error != null)
            return Fail<RedeemParentInviteCodeResultDto>(error, conflict: true);

        var pending = link!;
        _logger?.LogInformation("[ParentLinks] Veli bağlantı isteği (onay bekliyor): linkId={LinkId} parentId={ParentId} studentId={StudentId}",
            pending.Id, parentId, candidate.StudentId);

        return new RedeemParentInviteCodeResultDto
        {
            Success = true,
            ObjectId = pending.Id,
            Message = _localizer["parentLinks.requested"],
            Child = PendingChild(pending.Id, pending.CreatedAt)
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
            .Where(IsOpen(PendingCutoff(now)))
            .OrderBy(l => l.CreatedAt).ThenBy(l => l.Id)
            .Take(ParentLinkRules.MaxActiveChildrenPerParent)
            .Select(l => new
            {
                l.Id,
                l.Status,
                StudentUserId = l.Student.UserId,
                GradeName = l.Student.Grade != null ? l.Student.Grade.Name : null,
                // #361: yalnızca DOĞRULANMIŞ okul gösterilir — öğrencinin kendi seçtiği (bekleyen) okul ya da legacy serbest
                // metin SchoolName veliye "okulu" diye sunulmaz.
                SchoolName = l.Student.SchoolVerifiedAt != null && l.Student.School != null ? l.Student.School.Name : null,
                l.ActivatedAt,
                l.CreatedAt
            })
            .ToListAsync(ct);

        // Ad çözümü yalnızca Active çocuklar için — Pending'de öğrenci verisi dönmez (auth-api'ye id de gitmez).
        var activeRows = rows.Where(r => r.Status == ParentStudentLinkStatus.Active).ToList();
        var users = await LookupUsersAsync(activeRows.Select(r => r.StudentUserId), ct);
        return rows.Select(r => r.Status == ParentStudentLinkStatus.Active
            ? new LinkedChildDto
            {
                LinkId = r.Id,
                Status = nameof(ParentStudentLinkStatus.Active),
                StudentName = NameOf(users, r.StudentUserId, "parentLinks.fallbackStudentName"),
                GradeName = r.GradeName,
                SchoolName = r.SchoolName,
                LinkedAt = AsUtc(r.ActivatedAt ?? r.CreatedAt),
                RequestedAt = AsUtc(r.CreatedAt)
            }
            : PendingChild(r.Id, r.CreatedAt)).ToList();
    }

    private static LinkedChildDto PendingChild(int linkId, DateTime createdAt) => new()
    {
        LinkId = linkId,
        Status = nameof(ParentStudentLinkStatus.Pending),
        RequestedAt = AsUtc(createdAt),
        PendingExpiresAt = AsUtc(createdAt + ParentLinkRules.PendingValidity)
    };

    // ---- Koparma (iki taraf) ----------------------------------------------------------------------------------------

    public Task<ParentLinkResponseDto> RevokeAsync(int linkId, int userId, CancellationToken ct = default)
        => RevokeCoreAsync(linkId, userId, studentSideOnly: false, pendingOnly: false, "parentLinks.revoked", ct);

    /// <summary>
    /// Koparma ve retin TEK gövdesi. Koşullu UPDATE (<c>Status == okunan durum</c>) başarısızsa durum yeniden okunur
    /// (re-review A — eşzamanlı onay yarışı): Revoked → idempotent başarı; Pending iken Active olduysa koparma Active olarak
    /// TEKRARLANIR (ParentUnlinkedEvent yazılır), ret ise "artık bekleyen istek yok" → NotFound. Böylece HTTP sonucu ile DB
    /// durumu her sıralamada tutarlı: onay kazanırsa ya bağlantı sonra koparılır (event'li) ya da ret 404 alır.
    /// </summary>
    private async Task<ParentLinkResponseDto> RevokeCoreAsync(
        int linkId, int userId, bool studentSideOnly, bool pendingOnly, string okMessageKey, CancellationToken ct)
    {
        // Sahiplik: kullanıcı bu bağlantının öğrencisi (ret: yalnız öğrenci) ya da velisi olmalı; değilse varlığı sızdırmadan 404.
        var target = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.Id == linkId && (l.Student.UserId == userId || (!studentSideOnly && l.Parent.UserId == userId)))
            .Select(l => new
            {
                l.Id, l.Status, l.ParentId, l.StudentId,
                ParentUserId = l.Parent.UserId,
                StudentUserId = l.Student.UserId
            })
            .FirstOrDefaultAsync(ct);
        if (target == null)
            return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true);

        var revokedByRole = target.StudentUserId == userId ? "Student" : "Parent";
        var status = target.Status;
        for (var attempt = 0; attempt < MaxRevokeAttempts; attempt++)
        {
            if (status == ParentStudentLinkStatus.Revoked)
                return Ok(target.Id, okMessageKey);
            if (pendingOnly && status != ParentStudentLinkStatus.Pending)
                return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.NotFound, notFound: true); // aktif bağlantı: revoke

            var wasActive = status == ParentStudentLinkStatus.Active;
            var expected = status;
            var revokedNow = false;
            try
            {
                var strategy = _context.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    _context.ChangeTracker.Clear();
                    revokedNow = false;
                    var now = Now;

                    await using var tx = await _context.Database.BeginTransactionAsync(ct);
                    var affected = await _context.ParentStudentLinks
                        .Where(l => l.Id == target.Id && l.Status == expected)
                        .ExecuteUpdateAsync(set => set
                            .SetProperty(l => l.Status, ParentStudentLinkStatus.Revoked)
                            .SetProperty(l => l.RevokedAt, now)
                            .SetProperty(l => l.RevokedByUserId, userId), ct);
                    if (affected == 0)
                    {
                        await tx.CommitAsync(ct);
                        return;
                    }

                    // Unlinked event'i yalnızca gerçekten AKTİF olan bağlantı için (Pending iptali/reddi bildirim değil).
                    if (wasActive)
                    {
                        AddOutbox(new ParentUnlinkedEvent
                        {
                            EventId = Guid.NewGuid(),
                            LinkId = target.Id,
                            ParentId = target.ParentId,
                            ParentUserId = target.ParentUserId,
                            StudentId = target.StudentId,
                            StudentUserId = target.StudentUserId,
                            RevokedByRole = revokedByRole,
                            RevokedByUserId = userId,
                            RevokedAtUtc = now
                        }, now);
                        await _context.SaveChangesAsync(ct);
                    }
                    await tx.CommitAsync(ct);
                    revokedNow = true;
                });
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }

            if (revokedNow)
            {
                _logger?.LogInformation("[ParentLinks] Bağlantı koparıldı: linkId={LinkId} by={Role} wasActive={WasActive}",
                    target.Id, revokedByRole, wasActive);
                return Ok(target.Id, okMessageKey);
            }

            // Yarış: başka istek durumu değiştirdi (onay / ret / koparma) — güncel durumla yeniden karar ver.
            status = await StatusOfAsync(target.Id, ct) ?? ParentStudentLinkStatus.Revoked;
        }

        return Fail<ParentLinkResponseDto>(ParentLinkErrorCodes.Busy, conflict: true);
    }

    // ---- yardımcılar ------------------------------------------------------------------------------------------------

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
        // Hangi kısmın tuttuğu (yok / süresi dolmuş / kullanılmış / biçim / tavan / kendine bağlanma) ne yanıtta ne logda ayrışır.
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

    private T Fail<T>(string code, bool notFound = false, bool conflict = false) where T : ParentLinkResponseDto, new() => new()
    {
        Success = false,
        NotFound = notFound,
        Conflict = conflict,
        ErrorCode = code,
        Message = _localizer["parentLinks.errors." + char.ToLowerInvariant(code[0]) + code[1..]]
    };
}
