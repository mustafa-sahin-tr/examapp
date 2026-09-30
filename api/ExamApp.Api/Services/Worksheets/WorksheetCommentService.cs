using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.WorksheetComments;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace ExamApp.Api.Services.Worksheets;

/// <inheritdoc cref="IWorksheetCommentService"/>
public class WorksheetCommentService : IWorksheetCommentService
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    private readonly AppDbContext _context;
    private readonly IWorksheetResponsibleTeacherResolver _responsibleTeacher;
    private readonly IAuthApiClient _authApiClient;
    private readonly ILogger<WorksheetCommentService>? _logger;

    // Client'a dönen metinler mesaj sözlüğünden (issue #184). DI her zaman gerçek localizer'ı verir.
    private readonly IStringLocalizer<Messages> _localizer;

    /// <summary>
    /// Yazar adı çözümü (auth-api) için üst süre (code review O3). Aşılırsa adlar yerelleştirilmiş rol adına düşer; thread
    /// yine döner. Test için ayarlanabilir.
    /// </summary>
    internal TimeSpan AuthorNameLookupTimeout { get; init; } = TimeSpan.FromSeconds(2);

    public WorksheetCommentService(
        AppDbContext context,
        IWorksheetResponsibleTeacherResolver responsibleTeacher,
        IAuthApiClient authApiClient,
        ILogger<WorksheetCommentService>? logger = null,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _responsibleTeacher = responsibleTeacher;
        _authApiClient = authApiClient;
        _logger = logger;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    // ---- Okuma ----------------------------------------------------------------------------------------------

    public async Task<WorksheetCommentPageResultDto> GetThreadAsync(
        int worksheetId, WorksheetCommentQueryDto query, WorksheetCommentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);

        var worksheet = await LoadWorksheetAsync(worksheetId, ct);
        if (worksheet == null)
            return Fail<WorksheetCommentPageResultDto>(WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

        var access = await ResolveAccessAsync(worksheet, actor, ct);
        if (access.Denied is { } denied)
            return Fail<WorksheetCommentPageResultDto>(denied.Code, notFound: denied.NotFound, forbidden: !denied.NotFound);

        if (query.QuestionId.HasValue && !await IsWorksheetQuestionAsync(worksheetId, query.QuestionId.Value, ct))
            return Fail<WorksheetCommentPageResultDto>(WorksheetCommentErrorCodes.QuestionNotInWorksheet);

        (DateTime CreatedAt, int Id)? cursor = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            if (!TryDecodeCursor(query.Cursor, out var decoded))
                return Fail<WorksheetCommentPageResultDto>(WorksheetCommentErrorCodes.InvalidCursor);
            cursor = decoded;
        }

        var take = Math.Clamp(query.Take, 1, WorksheetCommentLimits.MaxPageSize);
        var questionId = query.QuestionId;

        var rootsQuery = _context.WorksheetComments
            .AsNoTracking()
            .Where(c => c.WorksheetId == worksheetId && c.ParentCommentId == null)
            .Where(c => questionId == null ? c.QuestionId == null : c.QuestionId == questionId);

        if (cursor is { } cur)
        {
            var (curTime, curId) = cur;
            rootsQuery = rootsQuery.Where(c => c.CreateTime < curTime || (c.CreateTime == curTime && c.Id < curId));
        }

        var roots = await rootsQuery
            .OrderByDescending(c => c.CreateTime)
            .ThenByDescending(c => c.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = roots.Count > take;
        if (hasMore)
            roots.RemoveAt(roots.Count - 1);

        var rootIds = roots.Select(r => r.Id).ToList();
        var replyCounts = new Dictionary<int, int>();
        var replies = new List<WorksheetComment>();
        if (rootIds.Count > 0)
        {
            replyCounts = await _context.WorksheetComments
                .AsNoTracking()
                .Where(c => c.ParentCommentId != null && rootIds.Contains(c.ParentCommentId.Value))
                .GroupBy(c => c.ParentCommentId!.Value)
                .Select(g => new { RootId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RootId, x => x.Count, ct);

            // Kök başına en yeni N reply: "benden daha yeni kardeş sayısı < N" (korelasyonlu alt sorgu,
            // IX (ParentCommentId, CreateTime, Id) üzerinden). APPLY/pencere fonksiyonu yok — SQLite ve PG'de aynı.
            var preview = WorksheetCommentLimits.RepliesPreviewCount;
            replies = await _context.WorksheetComments
                .AsNoTracking()
                .Where(c => c.ParentCommentId != null && rootIds.Contains(c.ParentCommentId.Value))
                .Where(c => _context.WorksheetComments.Count(o => o.ParentCommentId == c.ParentCommentId
                    && (o.CreateTime > c.CreateTime || (o.CreateTime == c.CreateTime && o.Id > c.Id))) < preview)
                .OrderBy(c => c.CreateTime)
                .ThenBy(c => c.Id)
                .ToListAsync(ct);
        }

        var authorNames = await ResolveAuthorNamesAsync(roots.Concat(replies).Select(c => c.AuthorUserId), ct);

        var studentLock = actor.Kind == WorksheetCommentActorKind.Student
            ? await StudentWriteLockAsync(worksheetId, questionId, access, ct)
            : null;

        var canWrite = actor.Kind switch
        {
            WorksheetCommentActorKind.Student => studentLock == null,
            WorksheetCommentActorKind.Teacher => access.TeacherCanCreateRoot,
            _ => false
        };

        var repliesByRoot = replies.GroupBy(r => r.ParentCommentId!.Value).ToDictionary(g => g.Key, g => g.ToList());

        var page = new WorksheetCommentPageDto
        {
            CanWrite = canWrite,
            LockReason = studentLock?.LockReason,
            NextCursor = hasMore ? EncodeCursor(roots[^1].CreateTime, roots[^1].Id) : null,
            Items = roots.Select(root =>
            {
                var thread = new WorksheetCommentThreadDto();
                Fill(thread, root, actor, authorNames);
                thread.Replies = repliesByRoot.TryGetValue(root.Id, out var list)
                    ? list.Select(r => Fill(new WorksheetCommentDto(), r, actor, authorNames)).ToList()
                    : new List<WorksheetCommentDto>();
                thread.ReplyCount = replyCounts.GetValueOrDefault(root.Id);
                thread.CanReply = CanReply(root, actor, access, canWrite);
                return thread;
            }).ToList()
        };

        return new WorksheetCommentPageResultDto { Success = true, Page = page };
    }

    public async Task<WorksheetCommentRepliesResultDto> GetRepliesAsync(
        int worksheetId, int rootId, WorksheetCommentRepliesQueryDto query, WorksheetCommentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);

        var worksheet = await LoadWorksheetAsync(worksheetId, ct);
        if (worksheet == null)
            return Fail<WorksheetCommentRepliesResultDto>(WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

        var access = await ResolveAccessAsync(worksheet, actor, ct);
        if (access.Denied is { } denied)
            return Fail<WorksheetCommentRepliesResultDto>(denied.Code, notFound: denied.NotFound, forbidden: !denied.NotFound);

        var root = await _context.WorksheetComments
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == rootId && c.WorksheetId == worksheetId && c.ParentCommentId == null, ct);
        if (root == null)
            return Fail<WorksheetCommentRepliesResultDto>(WorksheetCommentErrorCodes.RootCommentNotFound, notFound: true);

        (DateTime CreatedAt, int Id)? cursor = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            if (!TryDecodeCursor(query.Cursor, out var decoded))
                return Fail<WorksheetCommentRepliesResultDto>(WorksheetCommentErrorCodes.InvalidCursor);
            cursor = decoded;
        }

        var take = Math.Clamp(query.Take, 1, WorksheetCommentLimits.MaxPageSize);
        var repliesQuery = _context.WorksheetComments.AsNoTracking().Where(c => c.ParentCommentId == rootId);
        var replyCount = await repliesQuery.CountAsync(ct);

        if (cursor is { } cur)
        {
            var (curTime, curId) = cur;
            repliesQuery = repliesQuery.Where(c => c.CreateTime > curTime || (c.CreateTime == curTime && c.Id > curId));
        }

        var replies = await repliesQuery
            .OrderBy(c => c.CreateTime)
            .ThenBy(c => c.Id)
            .Take(take + 1)
            .ToListAsync(ct);

        var hasMore = replies.Count > take;
        if (hasMore)
            replies.RemoveAt(replies.Count - 1);

        var names = await ResolveAuthorNamesAsync(replies.Select(r => r.AuthorUserId), ct);
        var studentLock = actor.Kind == WorksheetCommentActorKind.Student
            ? await StudentWriteLockAsync(worksheetId, root.QuestionId, access, ct)
            : null;

        return new WorksheetCommentRepliesResultDto
        {
            Success = true,
            Page = new WorksheetCommentRepliesPageDto
            {
                Items = replies.Select(r => Fill(new WorksheetCommentDto(), r, actor, names)).ToList(),
                NextCursor = hasMore ? EncodeCursor(replies[^1].CreateTime, replies[^1].Id) : null,
                ReplyCount = replyCount,
                CanReply = CanReply(root, actor, access, studentCanWrite: studentLock == null)
            }
        };
    }

    // ---- Yazma ----------------------------------------------------------------------------------------------

    public async Task<WorksheetCommentResultDto> CreateAsync(
        int worksheetId, CreateWorksheetCommentDto dto, WorksheetCommentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.Kind == WorksheetCommentActorKind.AdminReader)
            return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);

        var worksheet = await LoadWorksheetAsync(worksheetId, ct);
        if (worksheet == null)
            return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

        var access = await ResolveAccessAsync(worksheet, actor, ct);
        if (access.Denied is { } denied)
            return Fail<WorksheetCommentResultDto>(denied.Code, notFound: denied.NotFound, forbidden: !denied.NotFound);

        switch (CommentBodySanitizer.TrySanitize(dto.Body, WorksheetComment.BodyMaxLength, out var body))
        {
            case CommentBodySanitizer.Outcome.Required:
                return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.BodyRequired);
            case CommentBodySanitizer.Outcome.InvalidCharacters:
                return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.BodyInvalidCharacters);
            case CommentBodySanitizer.Outcome.TooLong:
                return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.BodyTooLong);
        }

        if (dto.QuestionId.HasValue && !await IsWorksheetQuestionAsync(worksheetId, dto.QuestionId.Value, ct))
            return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.QuestionNotInWorksheet);

        WorksheetComment? parent = null;
        if (dto.ParentCommentId.HasValue)
        {
            parent = await _context.WorksheetComments
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == dto.ParentCommentId.Value, ct);

            // Tek seviye: parent aynı worksheet + aynı thread (QuestionId) içinde silinmemiş bir KÖK olmalı.
            if (parent == null || parent.WorksheetId != worksheetId || parent.QuestionId != dto.QuestionId
                || parent.ParentCommentId != null)
                return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.InvalidParent);
        }

        WorksheetCommentAuthorRole authorRole;
        int? responsibleTeacherUserId = null;
        if (actor.Kind == WorksheetCommentActorKind.Student)
        {
            var studentLock = await StudentWriteLockAsync(worksheetId, dto.QuestionId, access, ct);
            if (studentLock != null)
                return Fail<WorksheetCommentResultDto>(studentLock.ErrorCode, forbidden: true);
            authorRole = WorksheetCommentAuthorRole.Student;

            // Öğrenci KÖKÜ: ilgili öğretmen yazıldığı anda kayda sabitlenir (cevap yetkisi + dilim 2 bildirim hedefi).
            if (parent == null)
            {
                var responsible = await _responsibleTeacher.ResolveResponsibleTeacherAsync(
                    new ResponsibleTeacherWorksheet(worksheet.Id, worksheet.CreateUserId, worksheet.SourceWorksheetId),
                    actor.UserId, ct);
                responsibleTeacherUserId = responsible?.TeacherUserId;
            }
        }
        else
        {
            // Öğretmen: kapalı ayar öğretmeni engellemez. Kök → sahip/aktif atayan; reply → thread'in ilgili öğretmeni.
            var allowed = parent == null
                ? access.TeacherCanCreateRoot
                : TeacherCanReply(parent, actor, access);
            if (!allowed)
                return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.NotResponsibleTeacher, forbidden: true);
            authorRole = WorksheetCommentAuthorRole.Teacher;
        }

        var comment = new WorksheetComment
        {
            WorksheetId = worksheetId,
            QuestionId = dto.QuestionId,
            ParentCommentId = parent?.Id,
            AuthorUserId = actor.UserId,
            AuthorKeycloakId = actor.KeycloakId,
            AuthorRole = authorRole,
            ResponsibleTeacherUserId = responsibleTeacherUserId,
            Body = body
        };

        // Bildirim alıcıları (issue #105 dilim 2): TEK yazımdan önce; sub önce exam DB, gerekirse best-effort auth-api.
        var resolution = await ResolveNotificationRecipientsAsync(worksheet, comment, parent, actor, ct);
        var recipients = resolution.Recipients;

        _context.SetCurrentUser(actor.UserId);

        // Yorum + outbox satır(lar)ı tek transaction'da (yorum Id'si payload'da olduğundan iki SaveChanges,
        // TeacherApprovalService/TestSessionService ile aynı retry-güvenli desen).
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // Retry'da önceki denemenin tracker'a bıraktığı yorum (atanmış Id) ve outbox satırları temizlenmezse ikinci
            // deneme yanlış CommentId'li/çift outbox satırı yazar. Delegate içinde başka tracked entity kullanılmıyor
            // (worksheet/parent AsNoTracking; yalnız comment + outbox burada eklenir), Clear güvenlidir.
            _context.ChangeTracker.Clear();
            comment.Id = 0;

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            _context.WorksheetComments.Add(comment);
            await _context.SaveChangesAsync(ct);

            if (recipients.Count > 0)
            {
                AddNotificationOutbox(worksheet, comment, parent, actor, recipients);
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        });

        if (resolution.TeacherMissing)
        {
            _logger?.LogWarning(
                "[WorksheetComments] İlgili öğretmen çözülemedi; öğretmen bildirimi atlandı. CommentId={CommentId}, WorksheetId={WorksheetId}",
                comment.Id, worksheet.Id);
        }

        var names = new Dictionary<int, string?> { [actor.UserId] = actor.FullName };
        return new WorksheetCommentResultDto
        {
            Success = true,
            ObjectId = comment.Id,
            Message = _localizer["worksheets.comments.created"],
            Comment = Fill(new WorksheetCommentDto(), comment, actor, names)
        };
    }

    // ---- Bildirim (issue #105 dilim 2) ----------------------------------------------------------------------

    private enum NotificationKind { TeacherNotice, StudentReply }

    /// <summary>KeycloakId boş olabilir: exam DB'de yoksa consumer BadgeService verisinden çözer (sync auth-api çağrısı yok).</summary>
    private sealed record NotificationRecipient(NotificationKind Kind, int UserId, string KeycloakId);

    private sealed record RecipientResolution(List<NotificationRecipient> Recipients, bool TeacherMissing);

    /// <summary>
    /// Alıcı kuralları: öğrenci yazdıysa → ilgili öğretmen "Created" alır (öğrenci kökünde kökte sabitlenmiş
    /// <see cref="WorksheetComment.ResponsibleTeacherUserId"/>; öğretmen kökünde kökün yazarı VE reply yazan öğrencinin
    /// sorumlu öğretmeni); reply'sa ayrıca kök yazarı başka bir öğrenciyse o "Replied" alır. Öğretmen reply yazdıysa → kök
    /// yazarı öğrenci "Replied" alır. Öğretmen KÖKÜ (duyuru), öğretmen kökünün öğretmen cevabı ve kendi kendine bildirim YOK.
    /// Alıcılar tekil. Sub önce exam DB'den, bulunamayanlar için TEK toplu best-effort auth-api çağrısıyla (2 sn) çözülür; olmazsa boş kalır ve consumer çözer.
    /// Bildirim hazırlığındaki hata yorumu düşürmez: loglanır, event yazılmaz.
    /// </summary>
    private async Task<RecipientResolution> ResolveNotificationRecipientsAsync(
        Worksheet worksheet, WorksheetComment comment, WorksheetComment? parent, WorksheetCommentActor actor, CancellationToken ct)
    {
        try
        {
            var wanted = new List<(NotificationKind Kind, int UserId, string? Sub)>();
            var teacherMissing = false;

            if (actor.Kind == WorksheetCommentActorKind.Student)
            {
                var root = parent ?? comment;
                var teachers = new List<(int UserId, string? Sub)>();

                if (root.AuthorRole == WorksheetCommentAuthorRole.Student)
                {
                    if (root.ResponsibleTeacherUserId is > 0)
                        teachers.Add((root.ResponsibleTeacherUserId.Value, null));
                    else
                        teacherMissing = true;
                }
                else
                {
                    // Öğretmen kökü: kök yazarı + reply'ı yazan öğrencinin ilgili öğretmeni.
                    teachers.Add((root.AuthorUserId, root.AuthorKeycloakId));
                    var responsible = await _responsibleTeacher.ResolveResponsibleTeacherAsync(
                        new ResponsibleTeacherWorksheet(worksheet.Id, worksheet.CreateUserId, worksheet.SourceWorksheetId),
                        actor.UserId, ct);
                    if (responsible?.TeacherUserId is > 0)
                        teachers.Add((responsible.TeacherUserId, null));
                    else
                        teacherMissing = true;
                }

                foreach (var (userId, sub) in teachers)
                    wanted.Add((NotificationKind.TeacherNotice, userId, sub));

                if (parent != null && parent.AuthorRole == WorksheetCommentAuthorRole.Student)
                    wanted.Add((NotificationKind.StudentReply, parent.AuthorUserId, parent.AuthorKeycloakId));
            }
            else if (actor.Kind == WorksheetCommentActorKind.Teacher
                && parent != null && parent.AuthorRole == WorksheetCommentAuthorRole.Student)
            {
                wanted.Add((NotificationKind.StudentReply, parent.AuthorUserId, parent.AuthorKeycloakId));
            }

            // Kendine bildirim yok + tekilleştirme.
            var seen = new HashSet<int>();
            var result = new List<NotificationRecipient>();
            foreach (var (kind, userId, sub) in wanted)
            {
                if (userId <= 0 || userId == actor.UserId || !seen.Add(userId))
                    continue;

                var effectiveSub = sub;
                if (string.IsNullOrWhiteSpace(effectiveSub))
                {
                    // Aynı kullanıcının exam DB'deki en son yorumundan (yalnız Keycloak sub; auth-api çağrısı yok).
                    effectiveSub = await _context.WorksheetComments.AsNoTracking()
                        .Where(c => c.AuthorUserId == userId && c.AuthorKeycloakId != "")
                        .OrderByDescending(c => c.Id)
                        .Select(c => c.AuthorKeycloakId)
                        .FirstOrDefaultAsync(ct);
                }

                result.Add(new NotificationRecipient(kind, userId, effectiveSub ?? string.Empty));
            }

            // 2. Exam DB'de sub bulunamayanlar için TEK toplu auth-api çağrısı (best-effort, 2 sn; WorksheetAccessRequestService
            // ile aynı kabul edilmiş desen). Hata olursa sub boş kalır → consumer BadgeService verisinden çözer, son çare fırlatır.
            var blank = result.Where(r => string.IsNullOrWhiteSpace(r.KeycloakId)).Select(r => r.UserId).ToList();
            if (blank.Count > 0)
            {
                var resolved = await LookupSubsBestEffortAsync(blank, worksheet.Id, ct);
                if (resolved.Count > 0)
                {
                    result = result
                        .Select(r => string.IsNullOrWhiteSpace(r.KeycloakId) && resolved.TryGetValue(r.UserId, out var s)
                            ? r with { KeycloakId = s }
                            : r)
                        .ToList();
                }
            }

            return new RecipientResolution(result, teacherMissing);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger?.LogWarning(ex,
                "[WorksheetComments] Bildirim alıcıları hesaplanamadı; yorum kaydediliyor, bildirim atlanıyor. WorksheetId={WorksheetId}",
                worksheet.Id);
            return new RecipientResolution(new List<NotificationRecipient>(), false);
        }
    }

    private async Task<Dictionary<int, string>> LookupSubsBestEffortAsync(List<int> userIds, int worksheetId, CancellationToken ct)
    {
        var resolved = new Dictionary<int, string>();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(AuthorNameLookupTimeout);
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, timeout.Token);
            foreach (var user in users)
            {
                if (!string.IsNullOrWhiteSpace(user.KeycloakId))
                    resolved[user.Id] = user.KeycloakId;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger?.LogWarning(ex,
                "[WorksheetComments] Bildirim alıcısı sub'ı auth-api'den çözülemedi; event boş sub ile yazılacak. WorksheetId={WorksheetId}",
                worksheetId);
        }

        return resolved;
    }

    /// <summary>Alıcı başına bir outbox satırı (ChangeTracker'a ekler; çağıran SaveChanges yapar). Gövde/e-posta taşınmaz.</summary>
    private void AddNotificationOutbox(
        Worksheet worksheet, WorksheetComment comment, WorksheetComment? parent, WorksheetCommentActor actor,
        IReadOnlyList<NotificationRecipient> recipients)
    {
        var rootId = parent?.Id ?? comment.Id;
        var authorDisplayName = comment.AuthorRole == WorksheetCommentAuthorRole.Student
            ? FormatStudentDisplayName(actor.FullName) ?? string.Empty
            : string.Empty;
        var createdAt = DateTime.SpecifyKind(comment.CreateTime, DateTimeKind.Utc);

        foreach (var r in recipients)
        {
            object payload;
            string type;
            if (r.Kind == NotificationKind.TeacherNotice)
            {
                type = OutboxEventRegistry.NameFor<WorksheetCommentCreatedEvent>();
                payload = new WorksheetCommentCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    CommentId = comment.Id,
                    RootCommentId = rootId,
                    WorksheetId = worksheet.Id,
                    QuestionId = comment.QuestionId,
                    WorksheetTitle = worksheet.Name,
                    AuthorRole = comment.AuthorRole.ToString(),
                    AuthorDisplayName = authorDisplayName,
                    RecipientUserId = r.UserId,
                    RecipientKeycloakId = r.KeycloakId,
                    CreatedAtUtc = createdAt
                };
            }
            else
            {
                type = OutboxEventRegistry.NameFor<WorksheetCommentRepliedEvent>();
                payload = new WorksheetCommentRepliedEvent
                {
                    EventId = Guid.NewGuid(),
                    CommentId = comment.Id,
                    RootCommentId = rootId,
                    WorksheetId = worksheet.Id,
                    QuestionId = comment.QuestionId,
                    WorksheetTitle = worksheet.Name,
                    AuthorRole = comment.AuthorRole.ToString(),
                    AuthorDisplayName = authorDisplayName,
                    RecipientUserId = r.UserId,
                    RecipientKeycloakId = r.KeycloakId,
                    CreatedAtUtc = createdAt
                };
            }

            _context.OutboxMessages.Add(new OutboxMessage
            {
                Type = type,
                Content = JsonSerializer.Serialize(payload, payload.GetType()),
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    // ---- Yetki ----------------------------------------------------------------------------------------------

    private sealed record Denial(string Code, bool NotFound);

    private sealed class AccessContext
    {
        public Denial? Denied { get; init; }

        // Öğrenci
        public int StudentId { get; init; }
        public bool HasInstance { get; init; }
        public bool EffectiveCommentsEnabled { get; init; }

        // Öğretmen
        public bool TeacherCanCreateRoot { get; init; }
    }

    private sealed record StudentLock(string LockReason, string ErrorCode);

    private async Task<AccessContext> ResolveAccessAsync(Worksheet worksheet, WorksheetCommentActor actor, CancellationToken ct)
    {
        switch (actor.Kind)
        {
            case WorksheetCommentActorKind.Student:
                return await ResolveStudentAccessAsync(worksheet, actor, ct);
            case WorksheetCommentActorKind.Teacher:
                return await ResolveTeacherAccessAsync(worksheet, actor, ct);
            default:
                // Admin (öğretmen/öğrenci değil): okuma muafiyeti (CanView'deki admin dalıyla aynı); yazma yok.
                return actor.IsAdmin
                    ? new AccessContext()
                    : new AccessContext { Denied = new Denial(WorksheetCommentErrorCodes.WorksheetNotFound, NotFound: true) };
        }
    }

    /// <summary>
    /// Öğrenci okuma/yazma kapısı mevcut test başlatma kuralıyla aynıdır (<see cref="WorksheetAccess.CanStudentStartTest"/>,
    /// #14/#236): ilgili aktif atama VEYA grade uyumlu + Normal görünürlük. Ek olarak worksheet'i daha önce çözmüş öğrenci
    /// (instance'ı var) erişmeye devam eder — AC "aynı worksheet'i çözmüş (veya erişimi olan) tüm öğrenciler görür".
    /// Retire (soft-delete) edilmiş worksheet'te (code review O2 / security D1) yalnızca instance'ı olan öğrenci erişir;
    /// kimse artık başlatamayacağı için CanStudentStartTest yolu geçersizdir.
    /// </summary>
    private async Task<AccessContext> ResolveStudentAccessAsync(Worksheet worksheet, WorksheetCommentActor actor, CancellationToken ct)
    {
        var student = await _context.Students
            .AsNoTracking()
            .Where(s => s.UserId == actor.UserId)
            .OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.GradeId, s.SchoolId })
            .FirstOrDefaultAsync(ct);

        if (student == null)
            return new AccessContext { Denied = new Denial(WorksheetCommentErrorCodes.AccessDenied, NotFound: false) };

        var hasInstance = await _context.TestInstances
            .AsNoTracking()
            .AnyAsync(ti => ti.StudentId == student.Id && ti.WorksheetId == worksheet.Id, ct);

        // Atama override'ı retired worksheet'te de geçerli (atamalar worksheet'le birlikte silinmez).
        var relevantAssignment = await _responsibleTeacher.FindRelevantActiveAssignmentAsync(
            worksheet.Id, student.Id, student.GradeId, student.SchoolId, ct);
        var isGradeMatch = student.GradeId.HasValue && worksheet.GradeId == student.GradeId.Value;
        var canRead = worksheet.IsDeleted
            ? hasInstance
            : hasInstance || WorksheetAccess.CanStudentStartTest(relevantAssignment != null, isGradeMatch, worksheet.StudentVisibility);

        if (!canRead)
            return new AccessContext { Denied = new Denial(WorksheetCommentErrorCodes.AccessDenied, NotFound: false) };

        return new AccessContext
        {
            StudentId = student.Id,
            HasInstance = hasInstance,
            EffectiveCommentsEnabled = relevantAssignment?.CommentsEnabledOverride ?? worksheet.CommentsEnabled
        };
    }

    /// <summary>
    /// Öğretmen okuma: <see cref="WorksheetAccess.CanView"/> (sahip/admin/Public*/aynı okul SchoolOnly — detay ekranıyla aynı)
    /// VEYA bu worksheet'e atama yapmış olmak (paylaşım sonradan kapansa da atadığı öğrencilerin thread'ini görür).
    /// Görmüyorsa 404 (varlık sızdırılmaz, #11 deseni). Kök yorum: sahibi veya aktif ataması olan öğretmen.
    /// </summary>
    private async Task<AccessContext> ResolveTeacherAccessAsync(Worksheet worksheet, WorksheetCommentActor actor, CancellationToken ct)
    {
        var userId = actor.UserId;
        var (ownerSchoolId, requesterSchoolId) = await _context.ResolveSchoolContextAsync(worksheet, userId, actor.IsAdmin, ct);
        var canView = WorksheetAccess.CanView(worksheet.CreateUserId, userId, actor.IsAdmin, worksheet.TeacherSharing,
            worksheet.StudentVisibility, requesterSchoolId, ownerSchoolId);

        var ownAssignments = _context.WorksheetAssignments
            .AsNoTracking()
            .Where(a => a.WorksheetId == worksheet.Id && a.CreateUserId == userId);
        var hasActiveAssignment = await ownAssignments.Where(WorksheetAccess.ActiveAt(DateTime.UtcNow)).AnyAsync(ct);

        if (!canView && !hasActiveAssignment && !await ownAssignments.AnyAsync(ct))
            return new AccessContext { Denied = new Denial(WorksheetCommentErrorCodes.WorksheetNotFound, NotFound: true) };

        var isOwner = worksheet.CreateUserId.HasValue && worksheet.CreateUserId.Value > 0 && worksheet.CreateUserId.Value == userId;
        return new AccessContext { TeacherCanCreateRoot = isOwner || hasActiveAssignment };
    }

    /// <summary>Öğrenci için yazma kilidi (null = yazabilir). Sıra: ayar kapalı → başlatılmamış → cevaplanmamış.</summary>
    private async Task<StudentLock?> StudentWriteLockAsync(int worksheetId, int? questionId, AccessContext access, CancellationToken ct)
    {
        if (!access.EffectiveCommentsEnabled)
            return new StudentLock(WorksheetCommentLockReasons.CommentsDisabled, WorksheetCommentErrorCodes.CommentsDisabled);

        // Worksheet seviyesi (genel) thread: çözme şartı yok.
        if (!questionId.HasValue)
            return null;

        if (!access.HasInstance)
            return new StudentLock(WorksheetCommentLockReasons.WorksheetNotStarted, WorksheetCommentErrorCodes.WorksheetNotStarted);

        var studentId = access.StudentId;
        var qid = questionId.Value;
        var answered = await _context.TestInstanceQuestions
            .AsNoTracking()
            .AnyAsync(q => q.WorksheetInstance.StudentId == studentId
                && q.WorksheetInstance.WorksheetId == worksheetId
                && q.WorksheetQuestion.QuestionId == qid
                && (q.SelectedAnswerId != null || q.AnswerPayload != null), ct);

        return answered
            ? null
            : new StudentLock(WorksheetCommentLockReasons.QuestionNotAnswered, WorksheetCommentErrorCodes.QuestionNotAnswered);
    }

    private static bool CanReply(WorksheetComment root, WorksheetCommentActor actor, AccessContext access, bool studentCanWrite) =>
        actor.Kind switch
        {
            WorksheetCommentActorKind.Student => studentCanWrite,
            WorksheetCommentActorKind.Teacher => TeacherCanReply(root, actor, access),
            _ => false
        };

    /// <summary>
    /// Kök yazarı öğrenci → yalnızca kökte SABİTLENMİŞ ilgili öğretmen (<see cref="WorksheetComment.ResponsibleTeacherUserId"/>);
    /// anlık yeniden hesaplama yok — atama bitse de bildirim giden öğretmen cevap yazar. Kök yazarı öğretmen (duyuru) →
    /// kökün yazarı veya kök açabilen (sahip / aktif atayan) öğretmen.
    /// </summary>
    private static bool TeacherCanReply(WorksheetComment root, WorksheetCommentActor actor, AccessContext access)
    {
        if (root.AuthorRole == WorksheetCommentAuthorRole.Student)
            return root.ResponsibleTeacherUserId.HasValue && root.ResponsibleTeacherUserId.Value == actor.UserId;

        return root.AuthorUserId == actor.UserId || access.TeacherCanCreateRoot;
    }

    // ---- Yardımcılar ----------------------------------------------------------------------------------------

    /// <summary>Retire (soft-delete) edilmiş worksheet de döner — thread'ler görünür kalır.</summary>
    private Task<Worksheet?> LoadWorksheetAsync(int worksheetId, CancellationToken ct) =>
        _context.Worksheets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == worksheetId, ct);

    private Task<bool> IsWorksheetQuestionAsync(int worksheetId, int questionId, CancellationToken ct) =>
        _context.TestQuestions.AsNoTracking().AnyAsync(tq => tq.TestId == worksheetId && tq.QuestionId == questionId, ct);

    private WorksheetCommentDto Fill(WorksheetCommentDto target, WorksheetComment source, WorksheetCommentActor actor,
        IReadOnlyDictionary<int, string?> fullNames)
    {
        fullNames.TryGetValue(source.AuthorUserId, out var fullName);
        target.Id = source.Id;
        target.WorksheetId = source.WorksheetId;
        target.QuestionId = source.QuestionId;
        target.ParentCommentId = source.ParentCommentId;
        target.AuthorRole = source.AuthorRole;
        target.AuthorDisplayName = DisplayName(source.AuthorRole, fullName);
        target.IsMine = source.AuthorUserId == actor.UserId;
        target.Body = source.Body;
        target.CreatedAt = DateTime.SpecifyKind(source.CreateTime, DateTimeKind.Utc);
        return target;
    }

    private string DisplayName(WorksheetCommentAuthorRole role, string? fullName)
    {
        if (role == WorksheetCommentAuthorRole.Student)
        {
            var shortName = FormatStudentDisplayName(fullName);
            return shortName ?? _localizer["worksheets.comments.anonymousStudent"].Value;
        }

        return string.IsNullOrWhiteSpace(fullName)
            ? _localizer["worksheets.comments.anonymousTeacher"].Value
            : fullName.Trim();
    }

    /// <summary>
    /// Öğrenci görünen adı: "Ad S." — soyadın (son kelime) yalnızca baş harfi (Türkçe büyük harf kuralıyla: i → İ).
    /// Tek kelimelik adda yalnızca o kelime. Boş/null → null (çağıran yerelleştirilmiş "Öğrenci" yazar).
    /// </summary>
    internal static string? FormatStudentDisplayName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        var parts = fullName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0];

        var givenNames = string.Join(' ', parts[..^1]);
        var initial = char.ToUpper(parts[^1][0], TurkishCulture);
        return $"{givenNames} {initial}.";
    }

    /// <summary>
    /// Yazar adlarını auth-api'den toplu çözer (best-effort, <see cref="AuthorNameLookupTimeout"/> üst süreli). auth-api
    /// yavaş/erişilemez/devre açık ise boş sözlük — thread yine döner, adlar yerelleştirilmiş rol adına düşer. İsteğin
    /// kendisi (dış ct) iptal edildiyse iptal yayılır.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, string?>> ResolveAuthorNamesAsync(IEnumerable<int> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        var result = new Dictionary<int, string?>();
        if (ids.Count == 0)
            return result;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(AuthorNameLookupTimeout);
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(ids, timeout.Token);
            foreach (var user in users)
            {
                if (!string.IsNullOrWhiteSpace(user.FullName))
                    result[user.Id] = user.FullName;
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or JsonException
            or TimeoutRejectedException or BrokenCircuitException or OperationCanceledException)
        {
            _logger?.LogWarning(ex, "[WorksheetComments] Yazar adları çözülemedi; rol adına düşülüyor. count={Count}", ids.Count);
        }

        return result;
    }

    private T Fail<T>(string errorCode, bool notFound = false, bool forbidden = false) where T : WorksheetCommentResponseDto, new() =>
        new()
        {
            Success = false,
            NotFound = notFound,
            Forbidden = forbidden,
            ErrorCode = errorCode,
            Message = _localizer[$"worksheets.comments.errors.{char.ToLowerInvariant(errorCode[0])}{errorCode[1..]}"]
        };

    // Cursor: "{CreateTime.Ticks}:{Id}" base64url — istemci için opak. Sıralama (CreateTime, Id) ile birebir.
    internal static string EncodeCursor(DateTime createdAt, int id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
                string.Create(CultureInfo.InvariantCulture, $"{createdAt.Ticks}:{id}")))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static bool TryDecodeCursor(string cursor, out (DateTime CreatedAt, int Id) value)
    {
        value = default;
        try
        {
            var base64 = cursor.Trim().Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var parts = text.Split(':');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks || id <= 0)
                return false;

            value = (new DateTime(ticks, DateTimeKind.Utc), id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
