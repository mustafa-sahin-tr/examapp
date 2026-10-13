using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Parents;

/// <summary>Velinin onayladığı rıza metni sürümleri (issue #438). UI aynı sabiti gönderir; metin değişirse sürüm artar.</summary>
public static class ParentConsentVersions
{
    public const string Current = "kvkk-2026-10";
}

/// <summary>
/// Issue #438 (epic #435): veli panelinden çocuk hesabı açma ve çocuğun şifresini sıfırlama. Yalnız Parent rolü (controller);
/// sahiplik/bağlantı kararları burada.
/// </summary>
public interface IParentChildAccountService
{
    /// <summary>
    /// Akış: doğrulama (rıza, kullanıcı adı, parola, sınıf, okul) → tavanlar (veli başına en fazla
    /// <see cref="ParentChildAccountService.MaxChildAccountsPerParent"/> açılmış hesap + <see cref="ParentLinkRules.MaxActiveChildrenPerParent"/>
    /// açık çocuk) → auth-api Keycloak + identity hesabı → TEK transaction'da (veli kilidi altında) Student (ParentManaged) +
    /// ParentStudentLink (ParentCreated, Active, birincil) + ParentConsent + ParentLinkedEvent. auth-api hesabı oluştuktan sonra
    /// herhangi bir adım başarısız olursa hesap geri alınır (kompanzasyon). Parola hiçbir yanıtta/logda yer almaz.
    /// </summary>
    Task<ParentChildAccountResultDto> CreateAsync(int parentUserId, CreateChildAccountRequestDto request, string? ipAddress, CancellationToken ct = default);

    /// <summary>
    /// Velinin belirlediği yeni şifre. Yalnız çocuğa Active bağlı veli (aksi her durum 404 — IDOR) ve yalnız veli tarafından açılmış
    /// (<see cref="Student.ParentManaged"/>) hesap (aksi 403). Başarı <c>ParentAccessAudits</c>'e (<c>children.password-reset</c>) yazılır.
    /// </summary>
    Task<ParentLinkResponseDto> ResetPasswordAsync(int parentUserId, int studentId, string? newPassword, CancellationToken ct = default);
}

/// <inheritdoc cref="IParentChildAccountService"/>
public sealed class ParentChildAccountService : IParentChildAccountService
{
    /// <summary>Veli başına açılabilecek çocuk hesabı tavanı (kullanıcı kararı, #435) — silinmemiş öğrenciler sayılır.</summary>
    public const int MaxChildAccountsPerParent = 10;

    private static readonly HashSet<string> ParentLinkCodes = new(StringComparer.Ordinal)
    {
        ParentLinkErrorCodes.ProfileNotFound, ParentLinkErrorCodes.NotFound, ParentLinkErrorCodes.Busy,
        ParentLinkErrorCodes.ParentLimitReached
    };

    private readonly AppDbContext _context;
    private readonly IChildAccountDirectory _directory;
    private readonly IParentChildAccess _access;
    private readonly IAuthApiClient? _authApi;
    private readonly TimeProvider _time;
    private readonly ILogger<ParentChildAccountService>? _logger;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentChildAccountService(
        AppDbContext context,
        IChildAccountDirectory directory,
        IParentChildAccess access,
        IAuthApiClient? authApi = null,
        TimeProvider? time = null,
        ILogger<ParentChildAccountService>? logger = null,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _directory = directory;
        _access = access;
        _authApi = authApi;
        _time = time ?? TimeProvider.System;
        _logger = logger;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async Task<ParentChildAccountResultDto> CreateAsync(
        int parentUserId, CreateChildAccountRequestDto request, string? ipAddress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parentId = await _context.Parents.AsNoTracking()
            .Where(p => p.UserId == parentUserId)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync(ct);
        if (parentId == null)
            return Fail(ParentLinkErrorCodes.ProfileNotFound, notFound: true);

        // ---- Girdi (veri okumadan önce; sıra UI'nin alan hatası gösterme sırası) ------------------------------------------
        if (!request.ConsentAccepted)
            return Fail(ParentChildAccountErrorCodes.ConsentRequired);
        if (!string.Equals(request.ConsentVersion?.Trim(), ParentConsentVersions.Current, StringComparison.Ordinal))
            return Fail(ParentChildAccountErrorCodes.ConsentVersionMismatch);

        var username = ChildAccountRules.NormalizeUsername(request.Username);
        if (!ChildAccountRules.IsValidUsername(username))
            return Fail(ChildAccountErrorCodes.InvalidUsername);
        if (!ChildAccountRules.IsValidName(request.FirstName) || !ChildAccountRules.IsValidName(request.LastName))
            return Fail(ChildAccountErrorCodes.InvalidName);
        if (!ChildAccountRules.IsValidPassword(request.Password) || ChildAccountRules.PasswordContainsUsername(request.Password, username))
            return Fail(ChildAccountErrorCodes.PasswordRejected);

        if (!await _context.Grades.AsNoTracking().AnyAsync(g => g.Id == request.GradeId, ct))
            return Fail(ParentChildAccountErrorCodes.InvalidGrade);
        if (request.SchoolId is int sid && !await _context.Schools.AsNoTracking().AnyAsync(s => s.Id == sid, ct))
            return Fail(ParentChildAccountErrorCodes.SchoolNotFound);

        // ---- Tavanlar (ucuz ön kontrol: auth-api'ye gitmeden; kilit altında yeniden doğrulanır) ----------------------------
        var limit = await CheckLimitsAsync(parentId.Value, Now, ct);
        if (limit != null)
            return Fail(limit, conflict: true);

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        // Veli bildirim hedefi (ParentLinkedEvent) transaction DIŞINDA, fail-soft (V5 ile aynı desen).
        var parentNotify = ParentNotificationSupport.Of(
            await ParentNotificationSupport.LookupAsync(_authApi, new[] { parentUserId }, _logger, ct), parentUserId);

        // ---- Kimlik deposu (auth-api → Keycloak + identity) ----------------------------------------------------------------
        var account = new CreateChildAccountRequest { Username = username, FirstName = firstName, LastName = lastName };
        var pw = request.Password;
        account.Password = pw;
        var created = await _directory.CreateAsync(account, ct);
        switch (created.Status)
        {
            case ChildAccountCallStatus.Success:
                break;
            case ChildAccountCallStatus.Conflict:
                return Fail(ChildAccountErrorCodes.UsernameTaken, conflict: true);
            case ChildAccountCallStatus.Invalid:
                return Fail(KnownInvalidCode(created.ErrorCode));
            default:
                return Upstream();
        }

        var userId = created.Created!.UserId;
        var keycloakId = created.Created.KeycloakId;
        var studentName = $"{firstName} {lastName}";

        // ---- Geri dönüşsüz nokta: hesap var. Yerel yazım istemci iptaliyle kesilmesin; her hata hesabı geri alır. ----------
        string? error = null;
        Student? student = null;
        ParentStudentLink? link = null;
        try
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                error = null;
                student = null;
                link = null;
                _context.ChangeTracker.Clear();
                await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);
                await _context.Database.AcquireParentChildLinkLockAsync(parentId.Value, CancellationToken.None);

                var now = Now;
                error = await CheckLimitsAsync(parentId.Value, now, CancellationToken.None);
                if (error != null)
                    return; // geri alınır (commit yok)

                student = new Student
                {
                    UserId = userId,
                    StudentNumber = request.StudentNumber?.Trim() ?? string.Empty,
                    GradeId = request.GradeId,
                    // #361: velinin seçtiği okul BEKLEMEDE — doğrulama yalnız admin/okulun öğretmeni.
                    SchoolId = request.SchoolId,
                    ParentManaged = true
                };
                _context.Students.Add(student);
                await _context.SaveChangesAsync(CancellationToken.None);

                link = new ParentStudentLink
                {
                    ParentId = parentId.Value,
                    StudentId = student.Id,
                    Status = ParentStudentLinkStatus.Active,
                    Origin = ParentStudentLinkOrigin.ParentCreated,
                    IsPrimary = true,
                    CreatedAt = now,
                    ActivatedAt = now
                };
                _context.ParentStudentLinks.Add(link);
                _context.ParentConsents.Add(new ParentConsent
                {
                    ParentId = parentId.Value,
                    StudentId = student.Id,
                    Version = ParentConsentVersions.Current,
                    AcceptedAt = now,
                    IpAddress = ipAddress
                });
                await _context.SaveChangesAsync(CancellationToken.None);

                // Bağlantı doğrudan Active → V5 bildirimi (yalnız Active'e geçişte yazılır, #436).
                _context.OutboxMessages.Add(new OutboxMessage
                {
                    Type = OutboxEventRegistry.NameFor<ParentLinkedEvent>(),
                    Content = JsonSerializer.Serialize(new ParentLinkedEvent
                    {
                        EventId = Guid.NewGuid(),
                        LinkId = link.Id,
                        ParentId = parentId.Value,
                        ParentUserId = parentUserId,
                        StudentId = student.Id,
                        StudentUserId = userId,
                        ParentKeycloakId = parentNotify.KeycloakId,
                        StudentKeycloakId = keycloakId,
                        ParentDisplayName = parentNotify.DisplayName,
                        StudentDisplayName = Worksheets.WorksheetCommentService.FormatStudentDisplayName(studentName) ?? string.Empty,
                        LinkedAtUtc = now
                    }),
                    CreatedAt = now
                });
                await _context.SaveChangesAsync(CancellationToken.None);
                await tx.CommitAsync(CancellationToken.None);
            });
        }
        catch (Exception ex)
        {
            _context.ChangeTracker.Clear();
            _logger?.LogError(ex, "[ParentChildAccounts] Çocuk hesabı yerel kaydı yazılamadı; auth-api hesabı geri alınıyor (userId={UserId}).", userId);
            await CompensateAsync(userId);
            // Kilit zaman aşımı / yazım hatası (ör. eşzamanlı yarış, tekil index): hesap geri alındı, veli tekrar deneyebilir.
            if (ex is ParentLinkLockTimeoutException or DbUpdateException)
                return Fail(ParentLinkErrorCodes.Busy, conflict: true);
            throw;
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }

        if (error != null)
        {
            // Kilit altında tavan dolmuş (eşzamanlı ikinci istek): hesap geri alınır.
            await CompensateAsync(userId);
            return Fail(error, conflict: true);
        }

        _logger?.LogInformation(
            "[ParentChildAccounts] Veli çocuk hesabı açtı: parentId={ParentId} studentId={StudentId} linkId={LinkId}",
            parentId, student!.Id, link!.Id);
        return new ParentChildAccountResultDto
        {
            Success = true,
            ObjectId = student.Id,
            Message = _localizer["parentChildAccounts.created"],
            Child = new CreatedChildAccountDto(student.Id, link.Id, username, studentName)
        };
    }

    public async Task<ParentLinkResponseDto> ResetPasswordAsync(
        int parentUserId, int studentId, string? newPassword, CancellationToken ct = default)
    {
        // Kapı önce: bağlı olmayan veli (başkasının çocuğu, Pending/Revoked, silinmiş taraf) hangi koşulun tutmadığını öğrenemez.
        var grant = await _access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant == null)
            return FailBase(ParentLinkErrorCodes.NotFound, notFound: true);

        var managed = await _context.Students.AsNoTracking()
            .Where(s => s.Id == grant.StudentId)
            .Select(s => s.ParentManaged)
            .FirstOrDefaultAsync(ct);
        if (!managed)
            return FailBase(ParentChildAccountErrorCodes.NotParentManaged, forbidden: true);

        if (!ChildAccountRules.IsValidPassword(newPassword))
            return FailBase(ChildAccountErrorCodes.PasswordRejected);

        var result = await _directory.ResetPasswordAsync(grant.StudentUserId, newPassword!, ct);
        switch (result.Status)
        {
            case ChildAccountCallStatus.Success:
                break;
            case ChildAccountCallStatus.Invalid:
                return FailBase(ChildAccountErrorCodes.PasswordRejected);
            case ChildAccountCallStatus.NotFound:
                // Kimlik deposunda hesap yok ya da parent_managed değil — veliye "çocuk yok" ile aynı yanıt.
                _logger?.LogWarning("[ParentChildAccounts] auth-api çocuk hesabını bulamadı: studentId={StudentId}", grant.StudentId);
                return FailBase(ParentLinkErrorCodes.NotFound, notFound: true);
            default:
                return Upstream();
        }

        // Denetim izi: her sıfırlama ayrı satır (tekilleştirme yok); veri değişmez, yalnız kim/ne zaman.
        _context.ParentAccessAudits.Add(new ParentAccessAudit
        {
            ParentId = grant.ParentId,
            StudentId = grant.StudentId,
            Endpoint = ParentAccessEndpoints.ChildPasswordReset,
            At = Now
        });
        await _context.SaveChangesAsync(CancellationToken.None);

        _logger?.LogInformation("[ParentChildAccounts] Veli çocuğun şifresini sıfırladı: parentId={ParentId} studentId={StudentId}",
            grant.ParentId, grant.StudentId);
        return new ParentLinkResponseDto
        {
            Success = true,
            ObjectId = grant.StudentId,
            Message = _localizer["parentChildAccounts.passwordReset"]
        };
    }

    // ---- yardımcılar ------------------------------------------------------------------------------------------------

    /// <summary>Tavanlar: velinin açtığı (silinmemiş) çocuk hesabı sayısı ve açık çocuk bağlantısı sayısı. Doluysa hata kodu.</summary>
    private async Task<string?> CheckLimitsAsync(int parentId, DateTime now, CancellationToken ct)
    {
        var createdAccounts = await _context.ParentStudentLinks.AsNoTracking()
            .CountAsync(l => l.ParentId == parentId && l.Origin == ParentStudentLinkOrigin.ParentCreated && !l.Student.IsDeleted, ct);
        if (createdAccounts >= MaxChildAccountsPerParent)
            return ParentChildAccountErrorCodes.ChildAccountLimitReached;

        var open = await _context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == parentId)
            .Where(ParentLinkPrimary.IsOpen(now))
            .CountAsync(ct);
        return open >= ParentLinkRules.MaxActiveChildrenPerParent ? ParentLinkErrorCodes.ParentLimitReached : null;
    }

    private async Task CompensateAsync(int userId)
    {
        // İstemci bağlantıyı kesse de geri alma tamamlansın.
        var result = await _directory.DeleteAsync(userId, CancellationToken.None);
        if (result.Status is ChildAccountCallStatus.Success or ChildAccountCallStatus.NotFound)
        {
            _logger?.LogWarning("[ParentChildAccounts] Kompanzasyon: auth-api çocuk hesabı geri alındı (userId={UserId}).", userId);
            return;
        }
        // Yetim hesap: parent_managed işaretli, exam'de öğrencisi yok — operasyonel takip için Error.
        _logger?.LogError("[ParentChildAccounts] Kompanzasyon BAŞARISIZ: auth-api çocuk hesabı geri alınamadı (userId={UserId}, durum={Status}).",
            userId, result.Status);
    }

    /// <summary>auth-api'nin 400 kodu: bilinen alan hatası; bilinmiyorsa kullanıcı adı (Keycloak'ın kullanıcıyı reddi).</summary>
    private static string KnownInvalidCode(string? code) => code switch
    {
        ChildAccountErrorCodes.PasswordRejected => ChildAccountErrorCodes.PasswordRejected,
        ChildAccountErrorCodes.InvalidName => ChildAccountErrorCodes.InvalidName,
        _ => ChildAccountErrorCodes.InvalidUsername
    };

    private ParentChildAccountResultDto Upstream()
    {
        var dto = Fail(ChildAccountErrorCodes.Unavailable);
        dto.Upstream = true;
        return dto;
    }

    private ParentChildAccountResultDto Fail(string code, bool notFound = false, bool conflict = false, bool forbidden = false) => new()
    {
        Success = false,
        NotFound = notFound,
        Conflict = conflict,
        Forbidden = forbidden,
        ErrorCode = code,
        Message = MessageFor(code)
    };

    private ParentLinkResponseDto FailBase(string code, bool notFound = false, bool forbidden = false) => new()
    {
        Success = false,
        NotFound = notFound,
        Forbidden = forbidden,
        ErrorCode = code,
        Message = MessageFor(code)
    };

    private string MessageFor(string code)
    {
        var key = char.ToLowerInvariant(code[0]) + code[1..];
        var area = ParentLinkCodes.Contains(code) ? "parentLinks" : "parentChildAccounts";
        return code == ParentChildAccountErrorCodes.ChildAccountLimitReached
            ? _localizer[$"{area}.errors.{key}", MaxChildAccountsPerParent]
            : _localizer[$"{area}.errors.{key}"];
    }
}
