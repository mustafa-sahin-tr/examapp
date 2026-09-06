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
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>
/// Atama izni akışı (issue #13). Talep/onay/ret kayıtlarını yönetir ve her kararda ilgili
/// outbox event'ini iş işlemiyle atomik olarak yazar (talep + event ya birlikte kalır ya hiç).
/// auth-api isim/sub çözümü best-effort — erişilemezse event boş alanlarla yine de yazılır.
/// </summary>
public class WorksheetAccessRequestService : IWorksheetAccessRequestService
{
    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;
    private readonly ILogger<WorksheetAccessRequestService> _logger;

    public WorksheetAccessRequestService(
        AppDbContext context,
        IAuthApiClient authApiClient,
        ILogger<WorksheetAccessRequestService> logger)
    {
        _context = context;
        _authApiClient = authApiClient;
        _logger = logger;
    }

    public async Task<ResponseBaseDto> CreateRequestAsync(int worksheetId, string? note, int userId, string? userKeycloakId, bool isAdmin, CancellationToken ct = default)
    {
        var worksheet = await _context.Worksheets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == worksheetId, ct);

        // Görünmüyorsa varlığı sızdırma — atama akışındaki diğer metotlarla aynı "bulunamadı" deseni.
        if (worksheet == null ||
            !WorksheetAccess.CanView(worksheet.CreateUserId, userId, isAdmin, worksheet.TeacherSharing, worksheet.StudentVisibility))
        {
            return new ResponseBaseDto { Success = false, NotFound = true, Message = "Worksheet bulunamadı." };
        }

        if (worksheet.CreateUserId.HasValue && worksheet.CreateUserId.Value == userId)
        {
            return new ResponseBaseDto { Success = false, Message = "Kendi sınavınız için izin talebi oluşturamazsınız." };
        }

        var hasGrant = await _context.WorksheetAccessGrants
            .AnyAsync(g => g.WorksheetId == worksheetId && g.TeacherUserId == userId && g.RevokedAt == null, ct);

        if (WorksheetAccess.CanAssign(worksheet.CreateUserId, userId, isAdmin, worksheet.TeacherSharing, hasGrant))
        {
            return new ResponseBaseDto { Success = false, Message = "Bu sınavı zaten atayabilirsiniz." };
        }

        // Buraya kadar geldiyse PublicAssignable değil; yalnız PublicView için talep açılabilir.
        if (worksheet.TeacherSharing == WorksheetTeacherSharing.Private)
        {
            return new ResponseBaseDto { Success = false, Message = "Bu sınav paylaşıma kapalı." };
        }

        var pendingExists = await _context.WorksheetAccessRequests
            .AnyAsync(r => r.WorksheetId == worksheetId
                && r.RequesterUserId == userId
                && r.Status == WorksheetAccessRequestStatus.Pending, ct);

        if (pendingExists)
        {
            return new ResponseBaseDto { Success = false, Conflict = true, Message = "Bu sınav için zaten bekleyen bir talebiniz var." };
        }

        var ownerUserId = worksheet.CreateUserId ?? 0;
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        // auth-api lookup TEK yazım işleminden önce, best-effort. Patlarsa event alanları boş geçer
        // (consumer boş sub'ı LogWarning ile tolere ediyor) — talep + outbox yine de atomik yazılır.
        var requesterName = string.Empty;
        var targetKeycloakId = string.Empty;
        try
        {
            var lookup = await _authApiClient.GetUsersByIdsAsync(new[] { ownerUserId, userId }, ct);
            requesterName = lookup.FirstOrDefault(u => u.Id == userId)?.FullName ?? string.Empty;
            targetKeycloakId = lookup.FirstOrDefault(u => u.Id == ownerUserId)?.KeycloakId ?? string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex,
                "Atama izni talebi için auth-api lookup başarısız; event alanları boş geçilecek. WorksheetId={WorksheetId}",
                worksheetId);
        }

        _context.SetCurrentUser(userId);

        // Talep satırı + outbox mesajı tek transaction'da (retry-on-failure ile uyumlu olması için
        // execution strategy içinde — QuestionService ile aynı desen). request.Id identity DB'den
        // üretildiği için iki SaveChanges tek transaction ile atomik kılınır.
        var newRequestId = 0;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var request = new WorksheetAccessRequest
            {
                WorksheetId = worksheetId,
                RequesterUserId = userId,
                RequesterKeycloakId = userKeycloakId,
                Status = WorksheetAccessRequestStatus.Pending,
                Note = normalizedNote
            };
            _context.WorksheetAccessRequests.Add(request);
            await _context.SaveChangesAsync(ct);

            var @event = new WorksheetAccessRequestedEvent
            {
                RequestId = request.Id,
                WorksheetId = worksheet.Id,
                WorksheetName = worksheet.Name,
                RequesterUserId = userId,
                RequesterName = requesterName,
                OwnerUserId = ownerUserId,
                TargetKeycloakId = targetKeycloakId,
                Note = normalizedNote,
                RequestedAt = request.CreateTime
            };
            _context.OutboxMessages.Add(new OutboxMessage
            {
                Type = OutboxEventRegistry.NameFor<WorksheetAccessRequestedEvent>(),
                Content = JsonSerializer.Serialize(@event),
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            newRequestId = request.Id;
        });

        _logger.LogInformation(
            "WorksheetAccessRequest oluşturuldu. RequestId={RequestId}, WorksheetId={WorksheetId}, RequesterUserId={RequesterUserId}",
            newRequestId, worksheet.Id, userId);

        return new ResponseBaseDto { Success = true, ObjectId = newRequestId, Message = "Atama izni talebiniz sahibine iletildi." };
    }

    public async Task<List<WorksheetAccessRequestDto>> GetIncomingAsync(int ownerUserId, bool includeDecided, CancellationToken ct = default)
    {
        var query = from r in _context.WorksheetAccessRequests.AsNoTracking()
                    join w in _context.Worksheets.AsNoTracking() on r.WorksheetId equals w.Id
                    where w.CreateUserId == ownerUserId
                    select new { Request = r, WorksheetName = w.Name };

        if (!includeDecided)
        {
            query = query.Where(x => x.Request.Status == WorksheetAccessRequestStatus.Pending);
        }

        var rows = await query
            .OrderByDescending(x => x.Request.CreateTime)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return new List<WorksheetAccessRequestDto>();
        }

        var requesterIds = rows.Select(x => x.Request.RequesterUserId).Distinct().ToList();
        var nameById = new Dictionary<int, string>();
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(requesterIds, ct);
            nameById = users.ToDictionary(u => u.Id, u => u.FullName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Gelen atama izni talepleri için talep eden adları çözülemedi ({Count} id).", requesterIds.Count);
        }

        return rows.Select(x => new WorksheetAccessRequestDto
        {
            Id = x.Request.Id,
            WorksheetId = x.Request.WorksheetId,
            WorksheetName = x.WorksheetName,
            RequesterUserId = x.Request.RequesterUserId,
            RequesterName = nameById.TryGetValue(x.Request.RequesterUserId, out var name) ? name : string.Empty,
            Note = x.Request.Note,
            Status = x.Request.Status.ToString(),
            CreateTime = x.Request.CreateTime,
            DecisionAt = x.Request.DecisionAt
        }).ToList();
    }

    public async Task<int> GetIncomingPendingCountAsync(int ownerUserId, CancellationToken ct = default)
    {
        return await (from r in _context.WorksheetAccessRequests.AsNoTracking()
                      join w in _context.Worksheets.AsNoTracking() on r.WorksheetId equals w.Id
                      where w.CreateUserId == ownerUserId && r.Status == WorksheetAccessRequestStatus.Pending
                      select r.Id).CountAsync(ct);
    }

    public async Task<ResponseBaseDto> ApproveAsync(int requestId, int ownerUserId, bool isAdmin, CancellationToken ct = default)
    {
        var request = await _context.WorksheetAccessRequests
            .Include(r => r.Worksheet)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        // Varlığı sızdırma (issue #10 deseni): kayıt yok VEYA çağıran sahibi/admin değil → aynı 404.
        if (request == null || !WorksheetAccess.CanModify(request.Worksheet?.CreateUserId, ownerUserId, isAdmin))
        {
            return new ResponseBaseDto { Success = false, NotFound = true, Message = "Talep bulunamadı." };
        }

        if (request.Status != WorksheetAccessRequestStatus.Pending)
        {
            return new ResponseBaseDto { Success = false, Message = "Bu talep zaten yanıtlanmış." };
        }

        var now = DateTime.UtcNow;
        _context.SetCurrentUser(ownerUserId);

        request.Status = WorksheetAccessRequestStatus.Approved;
        request.DecisionAt = now;
        request.DecidedByUserId = ownerUserId;

        var activeGrant = await _context.WorksheetAccessGrants
            .FirstOrDefaultAsync(g => g.WorksheetId == request.WorksheetId
                && g.TeacherUserId == request.RequesterUserId
                && g.RevokedAt == null, ct);

        if (activeGrant == null)
        {
            _context.WorksheetAccessGrants.Add(new WorksheetAccessGrant
            {
                WorksheetId = request.WorksheetId,
                TeacherUserId = request.RequesterUserId,
                GrantedByUserId = ownerUserId,
                GrantedAt = now
            });
        }

        await AddDecisionOutboxAsync<WorksheetAccessRequestApprovedEvent>(request, now, ct);

        // Tek SaveChanges — status + grant + outbox aynı transaction'da.
        await _context.SaveChangesAsync(ct);

        return new ResponseBaseDto { Success = true, ObjectId = request.Id, Message = "Atama izni verildi." };
    }

    public async Task<ResponseBaseDto> RejectAsync(int requestId, int ownerUserId, bool isAdmin, CancellationToken ct = default)
    {
        var request = await _context.WorksheetAccessRequests
            .Include(r => r.Worksheet)
            .FirstOrDefaultAsync(r => r.Id == requestId, ct);

        if (request == null || !WorksheetAccess.CanModify(request.Worksheet?.CreateUserId, ownerUserId, isAdmin))
        {
            return new ResponseBaseDto { Success = false, NotFound = true, Message = "Talep bulunamadı." };
        }

        if (request.Status != WorksheetAccessRequestStatus.Pending)
        {
            return new ResponseBaseDto { Success = false, Message = "Bu talep zaten yanıtlanmış." };
        }

        var now = DateTime.UtcNow;
        _context.SetCurrentUser(ownerUserId);

        request.Status = WorksheetAccessRequestStatus.Rejected;
        request.DecisionAt = now;
        request.DecidedByUserId = ownerUserId;

        await AddDecisionOutboxAsync<WorksheetAccessRequestRejectedEvent>(request, now, ct);

        await _context.SaveChangesAsync(ct);

        return new ResponseBaseDto { Success = true, ObjectId = request.Id, Message = "Talep reddedildi." };
    }

    public async Task<ResponseBaseDto> RevokeGrantAsync(int worksheetId, int teacherUserId, int ownerUserId, bool isAdmin, CancellationToken ct = default)
    {
        var worksheet = await _context.Worksheets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == worksheetId, ct);

        // Varlığı sızdırma (issue #10 deseni): worksheet yok VEYA çağıran sahibi/admin değil → aynı 404.
        if (worksheet == null || !WorksheetAccess.CanModify(worksheet.CreateUserId, ownerUserId, isAdmin))
        {
            return new ResponseBaseDto { Success = false, NotFound = true, Message = "Worksheet bulunamadı." };
        }

        var grant = await _context.WorksheetAccessGrants
            .FirstOrDefaultAsync(g => g.WorksheetId == worksheetId
                && g.TeacherUserId == teacherUserId
                && g.RevokedAt == null, ct);

        if (grant == null)
        {
            return new ResponseBaseDto { Success = false, NotFound = true, Message = "Aktif bir atama izni bulunamadı." };
        }

        _context.SetCurrentUser(ownerUserId);
        grant.RevokedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return new ResponseBaseDto { Success = true, ObjectId = grant.Id, Message = "Atama izni geri alındı." };
    }

    /// <summary>
    /// Onay/ret kararında talep edene gidecek outbox event'ini ChangeTracker'a ekler (SaveChanges
    /// çağrının kendisinde). Talep edenin Keycloak sub'ı önce kayıttan alınır; yoksa auth-api'ye
    /// best-effort sorulur (patlarsa boş geçilir).
    /// </summary>
    private async Task AddDecisionOutboxAsync<TEvent>(WorksheetAccessRequest request, DateTime decidedAt, CancellationToken ct)
        where TEvent : class, new()
    {
        var targetKeycloakId = request.RequesterKeycloakId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetKeycloakId))
        {
            try
            {
                var users = await _authApiClient.GetUsersByIdsAsync(new[] { request.RequesterUserId }, ct);
                targetKeycloakId = users.FirstOrDefault(u => u.Id == request.RequesterUserId)?.KeycloakId ?? string.Empty;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                _logger.LogWarning(ex,
                    "Atama izni kararı için talep edenin Keycloak sub'ı çözülemedi; event boş sub ile yazılacak. RequestId={RequestId}",
                    request.Id);
            }
        }

        var worksheetName = request.Worksheet?.Name ?? string.Empty;

        object @event = typeof(TEvent) == typeof(WorksheetAccessRequestApprovedEvent)
            ? new WorksheetAccessRequestApprovedEvent
            {
                RequestId = request.Id,
                WorksheetId = request.WorksheetId,
                WorksheetName = worksheetName,
                RequesterUserId = request.RequesterUserId,
                TargetKeycloakId = targetKeycloakId,
                DecidedAt = decidedAt
            }
            : new WorksheetAccessRequestRejectedEvent
            {
                RequestId = request.Id,
                WorksheetId = request.WorksheetId,
                WorksheetName = worksheetName,
                RequesterUserId = request.RequesterUserId,
                TargetKeycloakId = targetKeycloakId,
                DecidedAt = decidedAt
            };

        _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<TEvent>(),
            Content = JsonSerializer.Serialize(@event),
            CreatedAt = DateTime.UtcNow
        });
    }
}
