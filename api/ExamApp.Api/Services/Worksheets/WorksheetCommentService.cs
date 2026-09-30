using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
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

        // Thread tek bir soruya (veya worksheet'e) ait: sıra istek başına TEK sorguyla çözülür, tüm yorumlara aynı değer yazılır.
        int? questionOrder = null;
        if (query.QuestionId.HasValue)
        {
            questionOrder = await ResolveQuestionOrderAsync(worksheetId, query.QuestionId.Value, ct);
            if (questionOrder == null)
                return Fail<WorksheetCommentPageResultDto>(WorksheetCommentErrorCodes.QuestionNotInWorksheet);
        }

        (DateTime CreatedAt, int Id)? cursor = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            if (!TryDecodeCursor(query.Cursor, out var decoded))
                return Fail<WorksheetCommentPageResultDto>(WorksheetCommentErrorCodes.InvalidCursor);
            cursor = decoded;
        }

        var take = Math.Clamp(query.Take, 1, WorksheetCommentLimits.MaxPageSize);
        var questionId = query.QuestionId;

        // issue #305: okul kapsamı — kök listesi, reply önizlemesi ve replyCount aynı filtreden geçer.
        var scope = VisibleTo(actor, access);
        var visible = _context.WorksheetComments.AsNoTracking().Where(scope);

        var rootsQuery = visible
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
            replyCounts = await visible
                .Where(c => c.ParentCommentId != null && rootIds.Contains(c.ParentCommentId.Value))
                .GroupBy(c => c.ParentCommentId!.Value)
                .Select(g => new { RootId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.RootId, x => x.Count, ct);

            // Kök başına en yeni N reply: "benden daha yeni (kapsamdaki) kardeş sayısı < N" (korelasyonlu alt sorgu,
            // IX (ParentCommentId, CreateTime, Id) üzerinden). APPLY/pencere fonksiyonu yok — SQLite ve PG'de aynı.
            var preview = WorksheetCommentLimits.RepliesPreviewCount;
            replies = await visible
                .Where(c => c.ParentCommentId != null && rootIds.Contains(c.ParentCommentId.Value))
                .Where(c => visible.Count(o => o.ParentCommentId == c.ParentCommentId
                    && (o.CreateTime > c.CreateTime || (o.CreateTime == c.CreateTime && o.Id > c.Id))) < preview)
                .OrderBy(c => c.CreateTime)
                .ThenBy(c => c.Id)
                .ToListAsync(ct);
        }

        var authorNames = await ResolveAuthorNamesAsync(roots.Concat(replies).Select(c => c.AuthorUserId), ct);
        var rootsById = roots.ToDictionary(r => r.Id);
        var view = await BuildViewAsync(actor, access, query.ModeratorView,
            roots.Select(r => (r, r)).Concat(replies.Select(r => (r, rootsById[r.ParentCommentId!.Value]))), ct);

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

        var studentSummary = actor.Kind == WorksheetCommentActorKind.Student
            ? null
            : await BuildStudentCommentsSummaryAsync(worksheet, actor, ct);

        var page = new WorksheetCommentPageDto
        {
            CanWrite = canWrite,
            LockReason = studentLock?.LockReason,
            QuestionOrder = questionOrder,
            StudentCommentsSummary = studentSummary,
            NextCursor = hasMore ? EncodeCursor(roots[^1].CreateTime, roots[^1].Id) : null,
            Items = roots.Select(root =>
            {
                var thread = new WorksheetCommentThreadDto();
                Fill(thread, root, root, view, authorNames, questionOrder);
                thread.Replies = repliesByRoot.TryGetValue(root.Id, out var list)
                    ? list.Select(r => Fill(new WorksheetCommentDto(), r, root, view, authorNames, questionOrder)).ToList()
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

        // issue #305: kapsam dışındaki kök de "yok" sayılır (varlık sızdırılmaz).
        var visible = _context.WorksheetComments.AsNoTracking().Where(VisibleTo(actor, access));
        var root = await visible
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
        var repliesQuery = visible.Where(c => c.ParentCommentId == rootId);
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
        var questionOrder = root.QuestionId is { } rootQuestionId
            ? await ResolveQuestionOrderAsync(worksheetId, rootQuestionId, ct)
            : null;
        var studentLock = actor.Kind == WorksheetCommentActorKind.Student
            ? await StudentWriteLockAsync(worksheetId, root.QuestionId, access, ct)
            : null;
        var view = await BuildViewAsync(actor, access, query.ModeratorView, replies.Select(r => (r, root)), ct);

        return new WorksheetCommentRepliesResultDto
        {
            Success = true,
            Page = new WorksheetCommentRepliesPageDto
            {
                Items = replies.Select(r => Fill(new WorksheetCommentDto(), r, root, view, names, questionOrder)).ToList(),
                NextCursor = hasMore ? EncodeCursor(replies[^1].CreateTime, replies[^1].Id) : null,
                ReplyCount = replyCount,
                QuestionOrder = questionOrder,
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

        int? questionOrder = null;
        if (dto.QuestionId.HasValue)
        {
            questionOrder = await ResolveQuestionOrderAsync(worksheetId, dto.QuestionId.Value, ct);
            if (questionOrder == null)
                return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.QuestionNotInWorksheet);
        }

        WorksheetComment? parent = null;
        if (dto.ParentCommentId.HasValue)
        {
            // issue #305: yazanın okul kapsamında olmayan kök "yok" sayılır (göremediği köke cevap yazamaz, varlık sızmaz).
            var parentId = dto.ParentCommentId.Value;
            parent = await _context.WorksheetComments
                .AsNoTracking()
                .Where(VisibleTo(actor, access))
                .FirstOrDefaultAsync(c => c.Id == parentId, ct);

            // Tek seviye: parent aynı worksheet + aynı thread (QuestionId) içinde silinmemiş bir KÖK olmalı.
            if (parent == null || parent.WorksheetId != worksheetId || parent.QuestionId != dto.QuestionId
                || parent.ParentCommentId != null)
                return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.InvalidParent);

            // issue #305: gizlenen köke yeni cevap yazılamaz (öğretmen dahil).
            if (parent.HiddenAt != null)
                return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.RootCommentHidden, forbidden: true);
        }

        WorksheetCommentAuthorRole authorRole;
        int? responsibleTeacherUserId = null;
        int? authorSchoolId = null;
        if (actor.Kind == WorksheetCommentActorKind.Student)
        {
            var studentLock = await StudentWriteLockAsync(worksheetId, dto.QuestionId, access, ct);
            if (studentLock != null)
                return Fail<WorksheetCommentResultDto>(studentLock.ErrorCode, forbidden: true);
            authorRole = WorksheetCommentAuthorRole.Student;
            authorSchoolId = access.ReaderSchoolId; // issue #305: yazarın okulu yorum anında sabitlenir

            // Öğrenci KÖKÜ: ilgili öğretmen yazıldığı anda kayda sabitlenir (cevap yetkisi + dilim 2 bildirim hedefi).
            // issue #305: öğretmen KÖKÜNE yazılan öğrenci reply'ında da (bildirim alan öğretmen okul kapsamı dışında olsa
            // bile o reply'ı görebilsin). Cevap yetkisi yalnız kökün değerinden gelir.
            if (parent == null || parent.AuthorRole == WorksheetCommentAuthorRole.Teacher)
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
            authorSchoolId = access.ReaderSchoolId; // issue #305 (Y1): öğretmen yorumunda da yazar okulu (bağımsız → null)
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
            AuthorSchoolId = authorSchoolId,
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
                AddNotificationOutbox(worksheet, comment, parent, actor, recipients, questionOrder);
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
        var view = new CommentView(actor, access, ModeratorView: false, new HashSet<int>(), new Dictionary<int, int>());
        return new WorksheetCommentResultDto
        {
            Success = true,
            ObjectId = comment.Id,
            Message = _localizer["worksheets.comments.created"],
            Comment = Fill(new WorksheetCommentDto(), comment, parent ?? comment, view, names, questionOrder)
        };
    }

    // ---- Moderasyon (issue #305) ----------------------------------------------------------------------------

    public async Task<WorksheetCommentReportResultDto> ReportAsync(
        int worksheetId, int commentId, ReportWorksheetCommentDto dto, WorksheetCommentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        ArgumentNullException.ThrowIfNull(actor);

        // Şikayet okuyuculara (öğrenci/öğretmen) açık; öğretmen olmayan admin doğrudan gizler.
        if (actor.Kind == WorksheetCommentActorKind.AdminReader)
            return Fail<WorksheetCommentReportResultDto>(WorksheetCommentErrorCodes.NotModerator, forbidden: true);

        var worksheet = await LoadWorksheetAsync(worksheetId, ct);
        if (worksheet == null)
            return Fail<WorksheetCommentReportResultDto>(WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

        var access = await ResolveAccessAsync(worksheet, actor, ct);
        if (access.Denied is { } denied)
            return Fail<WorksheetCommentReportResultDto>(denied.Code, notFound: denied.NotFound, forbidden: !denied.NotFound);

        if (!WorksheetCommentReportReasons.TryParse(dto.Reason, out var reason))
            return Fail<WorksheetCommentReportResultDto>(WorksheetCommentErrorCodes.InvalidReportReason);

        string? note = null;
        if (!string.IsNullOrWhiteSpace(dto.Note))
        {
            switch (CommentBodySanitizer.TrySanitize(dto.Note, WorksheetCommentReport.NoteMaxLength, out var cleaned))
            {
                case CommentBodySanitizer.Outcome.Ok:
                    note = cleaned;
                    break;
                case CommentBodySanitizer.Outcome.InvalidCharacters:
                    return Fail<WorksheetCommentReportResultDto>(WorksheetCommentErrorCodes.ModerationTextInvalidCharacters);
                case CommentBodySanitizer.Outcome.TooLong:
                    return Fail<WorksheetCommentReportResultDto>(WorksheetCommentErrorCodes.ReportNoteTooLong);
                // Required: yalnız görünmez karakterlerden oluşan not → notsuz şikayet.
            }
        }

        // Gizli yorum şikayet edilemez (içeriği okuyucuya görünmüyor; zaten moderatör elinde) → 404, varlık bilgisi
        // yer tutucuyla zaten açık olduğundan ek sızıntı yok.
        var target = await FindVisibleCommentAsync(worksheetId, commentId, actor, access, ct);
        if (target == null || target.Comment.HiddenAt != null)
            return Fail<WorksheetCommentReportResultDto>(WorksheetCommentErrorCodes.CommentNotFound, notFound: true);

        if (target.Comment.AuthorUserId == actor.UserId)
            return Fail<WorksheetCommentReportResultDto>(WorksheetCommentErrorCodes.CannotReportOwnComment, forbidden: true);

        var userId = actor.UserId;
        var alreadyReported = await _context.WorksheetCommentReports.AsNoTracking()
            .AnyAsync(r => r.CommentId == commentId && r.ReporterUserId == userId, ct);

        if (!alreadyReported)
        {
            _context.SetCurrentUser(actor.UserId);
            var report = new WorksheetCommentReport
            {
                CommentId = commentId,
                ReporterUserId = userId,
                ReporterKeycloakId = actor.KeycloakId,
                Reason = reason,
                Note = note
            };
            _context.WorksheetCommentReports.Add(report);
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Eşzamanlı ikinci istek unique index'e takıldı: ilk kayıt duruyorsa idempotent yanıt, değilse gerçek hata.
                if (!await ReportExistsAfterRaceAsync(report, commentId, userId, ct))
                    throw;
                alreadyReported = true;
            }
        }

        return new WorksheetCommentReportResultDto
        {
            Success = true,
            ObjectId = commentId,
            AlreadyReported = alreadyReported,
            ReportedByMe = true,
            Message = _localizer[alreadyReported ? "worksheets.comments.alreadyReported" : "worksheets.comments.reported"]
        };
    }

    private async Task<bool> ReportExistsAfterRaceAsync(WorksheetCommentReport pending, int commentId, int userId, CancellationToken ct)
    {
        _context.Entry(pending).State = EntityState.Detached;
        return await _context.WorksheetCommentReports.AsNoTracking()
            .AnyAsync(r => r.CommentId == commentId && r.ReporterUserId == userId, ct);
    }

    public async Task<WorksheetCommentResultDto> SetHiddenAsync(
        int worksheetId, int commentId, bool hidden, HideWorksheetCommentDto? dto, WorksheetCommentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.Kind == WorksheetCommentActorKind.Student)
            return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.NotModerator, forbidden: true);

        var worksheet = await LoadWorksheetAsync(worksheetId, ct);
        if (worksheet == null)
            return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

        var access = await ResolveAccessAsync(worksheet, actor, ct);
        if (access.Denied is { } denied)
            return Fail<WorksheetCommentResultDto>(denied.Code, notFound: denied.NotFound, forbidden: !denied.NotFound);

        string? reason = null;
        if (hidden)
        {
            switch (CommentBodySanitizer.TrySanitize(dto?.Reason, WorksheetComment.HiddenReasonMaxLength, out var cleaned))
            {
                case CommentBodySanitizer.Outcome.Required:
                    return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.HideReasonRequired);
                case CommentBodySanitizer.Outcome.InvalidCharacters:
                    return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.ModerationTextInvalidCharacters);
                case CommentBodySanitizer.Outcome.TooLong:
                    return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.HideReasonTooLong);
            }
            reason = cleaned;
        }

        // Moderatör de yorumu okul kapsamında görmeli: göremediği yorum "yok" (varlık sızdırılmaz). Kapsam dışındaki
        // yorumu thread'in sorumlu öğretmeni ya da admin yönetir.
        var target = await FindVisibleCommentAsync(worksheetId, commentId, actor, access, ct);
        if (target == null)
            return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.CommentNotFound, notFound: true);

        if (!CanModerate(target.Root, actor, access))
            return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.NotModerator, forbidden: true);

        var now = DateTime.UtcNow;
        DateTime? hiddenAt = hidden ? now : null;
        int? hiddenBy = hidden ? actor.UserId : null;
        var actorUserId = actor.UserId;

        // Yorum güncellemesi + audit satırı tek transaction'da. Durum zaten istenen gibiyse (tekrar gizle / gizli olmayanı
        // aç) idempotent: değişiklik ve audit yok. Neden audit'e YAZILMAZ (PII taşıyabilir) — yalnız yorumda durur.
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            var changed = await _context.WorksheetComments
                .Where(c => c.Id == commentId && (hidden ? c.HiddenAt == null : c.HiddenAt != null))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.HiddenAt, hiddenAt)
                    .SetProperty(c => c.HiddenByUserId, hiddenBy)
                    .SetProperty(c => c.HiddenReason, reason)
                    .SetProperty(c => c.UpdateTime, (DateTime?)now)
                    .SetProperty(c => c.UpdateUserId, (int?)actorUserId), ct);

            if (changed == 1)
            {
                _context.AdminUserActionLogs.Add(new AdminUserActionLog
                {
                    ActorKeycloakId = actor.KeycloakId,
                    Action = hidden ? AdminUserAction.CommentHidden : AdminUserAction.CommentUnhidden,
                    TargetType = AdminUserTargetType.WorksheetComment,
                    TargetId = commentId,
                    Outcome = AdminUserActionOutcome.Succeeded,
                    OccurredAtUtc = now
                });
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        });

        var fresh = await FindVisibleCommentAsync(worksheetId, commentId, actor, access, ct);
        if (fresh == null)
            return Fail<WorksheetCommentResultDto>(WorksheetCommentErrorCodes.CommentNotFound, notFound: true);

        var questionOrder = fresh.Comment.QuestionId is { } qid
            ? await ResolveQuestionOrderAsync(worksheetId, qid, ct)
            : null;
        var names = await ResolveAuthorNamesAsync(new[] { fresh.Comment.AuthorUserId }, ct);
        var view = await BuildViewAsync(actor, access, moderatorView: true, new[] { (fresh.Comment, fresh.Root) }, ct);

        return new WorksheetCommentResultDto
        {
            Success = true,
            ObjectId = commentId,
            Message = _localizer[hidden ? "worksheets.comments.hidden" : "worksheets.comments.unhidden"],
            Comment = Fill(new WorksheetCommentDto(), fresh.Comment, fresh.Root, view, names, questionOrder)
        };
    }

    public async Task<WorksheetCommentReportsResultDto> GetReportsAsync(
        int? worksheetId, WorksheetCommentReportsQueryDto query, WorksheetCommentActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(actor);

        if (actor.Kind == WorksheetCommentActorKind.Student)
            return Fail<WorksheetCommentReportsResultDto>(WorksheetCommentErrorCodes.NotModerator, forbidden: true);

        AccessContext access;
        IQueryable<WorksheetComment> comments = _context.WorksheetComments.AsNoTracking();
        if (worksheetId is { } wsId)
        {
            var worksheet = await LoadWorksheetAsync(wsId, ct);
            if (worksheet == null)
                return Fail<WorksheetCommentReportsResultDto>(WorksheetCommentErrorCodes.WorksheetNotFound, notFound: true);

            access = await ResolveAccessAsync(worksheet, actor, ct);
            if (access.Denied is { } denied)
                return Fail<WorksheetCommentReportsResultDto>(denied.Code, notFound: denied.NotFound, forbidden: !denied.NotFound);

            // Kapsam + kökün de kapsamda olması (security O1): göremediği thread'deki yorum listede yok.
            comments = VisibleWithRoot(actor, access).Where(c => c.WorksheetId == wsId);
            // Sahip ve admin worksheet'in (kapsamındaki) tüm yorumlarını yönetir; diğer öğretmen yalnız sorumlu olduğu thread'leri.
            if (!access.IsAdmin && !access.IsOwner)
                comments = comments.Where(ModeratedBy(actor.UserId));
        }
        else
        {
            // Global liste yalnız admin'e.
            if (!actor.IsAdmin)
                return Fail<WorksheetCommentReportsResultDto>(WorksheetCommentErrorCodes.NotModerator, forbidden: true);
            access = new AccessContext { IsAdmin = true };
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, WorksheetCommentLimits.MaxPageSize);
        var reports = _context.WorksheetCommentReports.AsNoTracking();

        // Şikayet tablosundan başla (code review): yorum başına tek GROUP BY (sayı + son şikayet Id'si), sonra yoruma join.
        // Global admin listesi tüm yorum tablosunu taramaz. Son şikayet önce: Id identity olduğundan Max(Id) = en son.
        var grouped = reports
            .GroupBy(r => r.CommentId)
            .Select(g => new { CommentId = g.Key, Count = g.Count(), LastReportId = g.Max(r => r.Id) });
        var reported =
            from g in grouped
            join c in comments on g.CommentId equals c.Id
            select new { Comment = c, Root = c.ParentComment, g.Count, g.LastReportId };

        var total = await reported.CountAsync(ct);
        var rows = await reported
            .OrderByDescending(x => x.LastReportId)
            .ThenByDescending(x => x.Comment.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<WorksheetCommentReportItemDto>();
        if (rows.Count > 0)
        {
            var ids = rows.Select(r => r.Comment.Id).ToList();
            var reasonCounts = await reports
                .Where(r => ids.Contains(r.CommentId))
                .GroupBy(r => new { r.CommentId, r.Reason })
                .Select(g => new { g.Key.CommentId, g.Key.Reason, Count = g.Count() })
                .ToListAsync(ct);

            var lastIds = rows.Select(r => r.LastReportId).ToList();
            var lastTimes = await reports
                .Where(r => lastIds.Contains(r.Id))
                .Select(r => new { r.CommentId, r.CreateTime })
                .ToDictionaryAsync(x => x.CommentId, x => x.CreateTime, ct);

            // Yorum başına en yeni N not ("benden yeni notlu şikayet sayısı < N", thread önizlemesiyle aynı desen).
            var maxNotes = WorksheetCommentReportReasons.MaxNotesPerItem;
            var notes = await reports
                .Where(r => ids.Contains(r.CommentId) && r.Note != null)
                .Where(r => reports.Count(o => o.CommentId == r.CommentId && o.Note != null && o.Id > r.Id) < maxNotes)
                .OrderByDescending(r => r.Id)
                .Select(r => new { r.CommentId, r.Note })
                .ToListAsync(ct);

            var worksheetIds = rows.Select(r => r.Comment.WorksheetId).Distinct().ToList();
            var titles = await _context.Worksheets.IgnoreQueryFilters().AsNoTracking()
                .Where(w => worksheetIds.Contains(w.Id))
                .Select(w => new { w.Id, w.Name })
                .ToDictionaryAsync(w => w.Id, w => w.Name, ct);

            // Soru numarası: sayfadaki tüm (worksheet, soru) çiftleri için TEK sorgu (code review O2: N+1 yok).
            var questionOrders = await WorksheetQuestionNumbering.ResolveNumbersAsync(_context,
                rows.Where(r => r.Comment.QuestionId.HasValue).Select(r => (r.Comment.WorksheetId, r.Comment.QuestionId!.Value)), ct);

            var names = await ResolveAuthorNamesAsync(rows.Select(r => r.Comment.AuthorUserId), ct);
            var counts = rows.ToDictionary(r => r.Comment.Id, r => r.Count);
            var view = new CommentView(actor, access, ModeratorView: true,
                await ReportedByMeAsync(actor, ids, ct), counts);

            foreach (var row in rows)
            {
                var c = row.Comment;
                int? questionOrder = c.QuestionId is { } q && questionOrders.TryGetValue((c.WorksheetId, q), out var n) ? n : null;
                var mine = reasonCounts.Where(x => x.CommentId == c.Id).ToList();
                items.Add(new WorksheetCommentReportItemDto
                {
                    Comment = Fill(new WorksheetCommentDto(), c, row.Root ?? c, view, names, questionOrder),
                    WorksheetTitle = titles.GetValueOrDefault(c.WorksheetId) ?? string.Empty,
                    ReportCount = counts.GetValueOrDefault(c.Id),
                    Reasons = new WorksheetCommentReportReasonCountsDto
                    {
                        Spam = mine.Where(x => x.Reason == WorksheetCommentReportReason.Spam).Sum(x => x.Count),
                        Abuse = mine.Where(x => x.Reason == WorksheetCommentReportReason.Abuse).Sum(x => x.Count),
                        PersonalInfo = mine.Where(x => x.Reason == WorksheetCommentReportReason.PersonalInfo).Sum(x => x.Count),
                        Other = mine.Where(x => x.Reason == WorksheetCommentReportReason.Other).Sum(x => x.Count)
                    },
                    LastReportedAt = DateTime.SpecifyKind(lastTimes.GetValueOrDefault(c.Id), DateTimeKind.Utc),
                    Notes = notes.Where(n => n.CommentId == c.Id).Select(n => n.Note!).ToList()
                });
            }
        }

        return new WorksheetCommentReportsResultDto
        {
            Success = true,
            Page = new WorksheetCommentReportsPageDto { Items = items, Page = page, PageSize = pageSize, TotalCount = total }
        };
    }

    private sealed record CommentWithRoot(WorksheetComment Comment, WorksheetComment Root);

    /// <summary>Bu worksheet'in, istek sahibinin okul kapsamındaki yorumu + kökü (reply değilse kendisi); yoksa null.</summary>
    private async Task<CommentWithRoot?> FindVisibleCommentAsync(
        int worksheetId, int commentId, WorksheetCommentActor actor, AccessContext access, CancellationToken ct)
    {
        // Reply ise kökü de kapsamda olmalı (security O1): kapsam dışı öğrenci kökündeki öğretmen cevabı "yok".
        var row = await VisibleWithRoot(actor, access)
            .Where(c => c.Id == commentId && c.WorksheetId == worksheetId)
            .Select(c => new { Comment = c, Root = c.ParentComment })
            .FirstOrDefaultAsync(ct);
        if (row == null)
            return null;

        // Reply'ın kökü soft-delete edildiyse (global filtre) reply de bağlamsız kalır — "yok" say.
        if (row.Comment.ParentCommentId != null && row.Root == null)
            return null;

        return new CommentWithRoot(row.Comment, row.Root ?? row.Comment);
    }

    // ---- Bildirim (issue #105 dilim 2) ----------------------------------------------------------------------

    private enum NotificationKind { TeacherNotice, StudentReply }

    /// <summary>KeycloakId boş olabilir: exam DB'de yoksa consumer BadgeService verisinden çözer (sync auth-api çağrısı yok).</summary>
    private sealed record NotificationRecipient(NotificationKind Kind, int UserId, string KeycloakId);

    private sealed record RecipientResolution(List<NotificationRecipient> Recipients, bool TeacherMissing);

    /// <summary>
    /// Alıcı kuralları: öğrenci yazdıysa → ilgili öğretmen "Created" alır (öğrenci kökünde kökte sabitlenmiş
    /// <see cref="WorksheetComment.ResponsibleTeacherUserId"/>; öğretmen kökünde reply yazan öğrencinin sabitlenmiş sorumlu
    /// öğretmeni ve — yalnız öğrenciyle aynı okuldaysa (#305 Y1) — kökün yazarı); reply'sa ayrıca kök yazarı başka bir öğrenciyse o "Replied" alır. Öğretmen reply yazdıysa → kök
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
                    // Öğretmen kökü: kök yazarı + reply'ı yazan öğrencinin ilgili öğretmeni (#305: CreateAsync reply'a
                    // sabitledi — bildirim alan öğretmen okul kapsamı dışında olsa da bu reply'ı görür).
                    // security Y1: kök yazarı öğretmen yalnız reply yazarıyla AYNI okuldaysa alıcı (okul dışı reply'ı
                    // göremez; okullar arası kanal olmasın). Bağımsız (okulsuz) kök yazarı ya da okulsuz öğrenci → alıcı değil.
                    if (root.AuthorSchoolId.HasValue && root.AuthorSchoolId == comment.AuthorSchoolId)
                        teachers.Add((root.AuthorUserId, root.AuthorKeycloakId));
                    if (comment.ResponsibleTeacherUserId is > 0)
                        teachers.Add((comment.ResponsibleTeacherUserId.Value, null));
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
        IReadOnlyList<NotificationRecipient> recipients, int? questionOrder)
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
                    QuestionOrder = questionOrder,
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
                    QuestionOrder = questionOrder,
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

        // issue #305 (okul kapsamı + moderasyon)
        /// <summary>Admin okuyucu (AdminReader ya da Admin rollü öğretmen): kapsam filtresi yok, her yorumun moderatörü.</summary>
        public bool IsAdmin { get; init; }

        /// <summary>İstek sahibi öğretmen ve worksheet'in sahibi.</summary>
        public bool IsOwner { get; init; }

        /// <summary>Okuyucunun exam DB'deki okulu (Students/Teachers.SchoolId); okulsuz/bağımsızsa null.</summary>
        public int? ReaderSchoolId { get; init; }

        /// <summary>Worksheet sahibi (legacy 0/null → null).</summary>
        public int? OwnerUserId { get; init; }

        /// <summary>Öğrenci okuyucunun ilgili öğretmeni (ilgili aktif atamayı yapan, yoksa sahip) — Y1 kapsamı.</summary>
        public int? StudentResponsibleTeacherUserId { get; init; }
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
                    ? new AccessContext { IsAdmin = true }
                    : new AccessContext { Denied = new Denial(WorksheetCommentErrorCodes.WorksheetNotFound, NotFound: true) };
        }
    }

    /// <summary>
    /// Öğrenci okuma/yazma kapısı mevcut test başlatma kuralıyla aynıdır (<see cref="WorksheetAccess.CanStudentStartTest"/>,
    /// #14/#236): ilgili aktif atama VEYA grade uyumlu + Normal görünürlük. Ek olarak worksheet'i daha önce çözmüş öğrenci
    /// (instance'ı var) erişmeye devam eder — AC "aynı worksheet'i çözmüş (veya erişimi olan) tüm öğrenciler görür".
    /// Retire (soft-delete) edilmiş worksheet'te (code review O2 / security D1) yalnızca instance'ı olan öğrenci erişir;
    /// kimse artık başlatamayacağı için CanStudentStartTest yolu geçersizdir.
    /// issue #305 (security D2): erişim yoksa 403 değil 404 <see cref="WorksheetCommentErrorCodes.WorksheetNotFound"/> —
    /// "var ama erişemezsin" ile "yok" ayırt edilemez (worksheet id taraması kapanır; #255 test başlatma ile hizalı).
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
            return new AccessContext { Denied = new Denial(WorksheetCommentErrorCodes.WorksheetNotFound, NotFound: true) };

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
            return new AccessContext { Denied = new Denial(WorksheetCommentErrorCodes.WorksheetNotFound, NotFound: true) };

        // issue #305 (Y1): öğrencinin ilgili öğretmeni — resolver ile aynı öncelik (ilgili aktif atamayı yapan > worksheet
        // sahibi; kopyada sahip zaten kopyalayandır). Ek sorgu yok: atama yukarıda çözüldü.
        var ownerUserId = worksheet.CreateUserId is > 0 ? worksheet.CreateUserId : null;
        var responsibleTeacherUserId = relevantAssignment?.CreateUserId is > 0 ? relevantAssignment.CreateUserId : ownerUserId;

        return new AccessContext
        {
            StudentId = student.Id,
            ReaderSchoolId = student.SchoolId,
            OwnerUserId = ownerUserId,
            StudentResponsibleTeacherUserId = responsibleTeacherUserId,
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

        // Tek sorgu: istekçinin okulu (#305 kapsam + yazar okulu) ve SchoolOnly kararı için sahibin okulu. Eskiden
        // ResolveSchoolContextAsync + ayrı okul sorgusu vardı (code review: çift sorgu). Admin'in okulu da okunur —
        // yazdığı öğretmen yorumuna AuthorSchoolId sabitlenir; okuma kapsamı admin'de uygulanmaz.
        var ownerId = worksheet.CreateUserId is > 0 ? worksheet.CreateUserId : null;
        var schoolRows = await _context.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId || (ownerId != null && t.UserId == ownerId))
            .OrderBy(t => t.Id)
            .Select(t => new { t.UserId, t.SchoolId })
            .ToListAsync(ct);
        var readerSchoolId = schoolRows.FirstOrDefault(r => r.UserId == userId)?.SchoolId;

        // ResolveSchoolContextAsync ile aynı anlam: okul çifti yalnız SchoolOnly + admin değil + sahip değil ise karar girdisi.
        var schoolDecision = worksheet.TeacherSharing == WorksheetTeacherSharing.SchoolOnly && !actor.IsAdmin
            && ownerId.HasValue && ownerId.Value != userId;
        var canView = WorksheetAccess.CanView(worksheet.CreateUserId, userId, actor.IsAdmin, worksheet.TeacherSharing,
            worksheet.StudentVisibility,
            schoolDecision ? readerSchoolId : null,
            schoolDecision ? schoolRows.FirstOrDefault(r => r.UserId == ownerId!.Value)?.SchoolId : null);

        var ownAssignments = _context.WorksheetAssignments
            .AsNoTracking()
            .Where(a => a.WorksheetId == worksheet.Id && a.CreateUserId == userId);
        var hasActiveAssignment = await ownAssignments.Where(WorksheetAccess.ActiveAt(DateTime.UtcNow)).AnyAsync(ct);

        if (!canView && !hasActiveAssignment && !await ownAssignments.AnyAsync(ct))
            return new AccessContext { Denied = new Denial(WorksheetCommentErrorCodes.WorksheetNotFound, NotFound: true) };

        var isOwner = ownerId.HasValue && ownerId.Value == userId;

        return new AccessContext
        {
            TeacherCanCreateRoot = isOwner || hasActiveAssignment,
            IsAdmin = actor.IsAdmin,
            IsOwner = isOwner,
            OwnerUserId = ownerId,
            ReaderSchoolId = readerSchoolId
        };
    }

    /// <summary>
    /// issue #305 — okul kapsamı (PO kararı: ad maskeleme değil, kısıtlama). <see cref="WorksheetComment.AuthorSchoolId"/>
    /// yorum anında sabitlenen yazar okuludur (öğrencide Students, öğretmende Teachers.SchoolId; okulsuz/bağımsızda null).
    /// <list type="bullet">
    /// <item>Öğrenci okuyucu: kendi yorumları; aynı okuldaki öğrencilerin yorumları; öğretmen yorumlarından yalnız
    /// (a) aynı okuldaki öğretmeninki, (b) worksheet sahibininki (içerik yazarının duyurusu), (c) kendi ilgili öğretmeninin
    /// (ilgili aktif atamayı yapan, yoksa sahip — istek başına bir kez çözülür, SQL'e sabit olarak girer) ve (d) bir öğrenci
    /// kökündeki öğretmen cevabı (öğrenci köküne yalnız o thread'in sabitlenmiş sorumlusu cevap yazabilir; kök görünür
    /// değilse reply de görünmez — bkz. <see cref="VisibleWithRoot"/>). Okulsuz okuyucu/yazar: okul eşleşmesi yok (güvenli taraf).</item>
    /// <item>Öğretmen okuyucu: tüm öğretmen yorumları (PO: okul dışı öğretmen yalnız öğretmen yorumlarını görür); kendi
    /// okulundaki öğrencilerin yorumları; kendisine sabitlenmiş öğrenci yorumları (öğrenci kökü + öğretmen köküne yazılan
    /// öğrenci reply'ı) ve sorumlusu olduğu ÖĞRENCİ kökündeki öğrenci reply'ları (bu reply'ları yazanlar kökü görebildiği
    /// için kök yazarıyla aynı okuldadır). Öğretmen KÖKÜNÜN yazarı olmak okul dışı öğrenci reply'larını görmek için YETMEZ
    /// (security Y1: okullar arası kanal). Worksheet SAHİBİ de bu kurala tabidir.</item>
    /// <item>Admin: hepsi.</item>
    /// </list>
    /// Okuma/yazma yolları bu ifadeden geçer; reply'ın kökü de görünür olmalıdır (<see cref="VisibleWithRoot"/>).
    /// </summary>
    private static Expression<Func<WorksheetComment, bool>> VisibleTo(WorksheetCommentActor actor, AccessContext access)
    {
        if (access.IsAdmin)
            return c => true;

        const WorksheetCommentAuthorRole student = WorksheetCommentAuthorRole.Student;
        const WorksheetCommentAuthorRole teacher = WorksheetCommentAuthorRole.Teacher;
        var uid = actor.UserId;
        // Okulsuz okuyucuda eşleşmeyecek bir değer: AuthorSchoolId null ise karşılaştırma zaten false; -1 hiçbir okul değil.
        var school = access.ReaderSchoolId ?? -1;

        if (actor.Kind == WorksheetCommentActorKind.Student)
        {
            var owner = access.OwnerUserId ?? -1;
            var responsible = access.StudentResponsibleTeacherUserId ?? -1;
            return c => c.AuthorUserId == uid
                || c.AuthorSchoolId == school
                || (c.AuthorRole == teacher
                    && (c.AuthorUserId == owner
                        || c.AuthorUserId == responsible
                        || (c.ParentComment != null && c.ParentComment.AuthorRole == student)));
        }

        return c => c.AuthorRole == teacher
            || c.AuthorSchoolId == school
            || c.ResponsibleTeacherUserId == uid
            || (c.ParentComment != null
                && c.ParentComment.AuthorRole == student
                && c.ParentComment.ResponsibleTeacherUserId == uid);
    }

    /// <summary>
    /// <see cref="VisibleTo"/> + reply ise kökü de görünür (security O1 / code O1): kapsam dışı bir kökün altındaki yorum
    /// (ör. okul dışı öğrenci kökündeki öğretmen cevabı) ne hedef olarak bulunur ne de listelenir.
    /// </summary>
    private IQueryable<WorksheetComment> VisibleWithRoot(WorksheetCommentActor actor, AccessContext access)
    {
        var scope = VisibleTo(actor, access);
        var comments = _context.WorksheetComments.AsNoTracking().Where(scope);
        if (access.IsAdmin)
            return comments;

        var visibleRoots = _context.WorksheetComments.Where(scope);
        return comments.Where(c => c.ParentCommentId == null || visibleRoots.Any(r => r.Id == c.ParentCommentId));
    }

    /// <summary>
    /// issue #305: thread'in sorumlu öğretmeni — öğrenci kökünde sabitlenmiş ilgili öğretmen, öğretmen kökünde (duyuru)
    /// kökün yazarı. Reply'lar kökün değerini kullanır.
    /// </summary>
    private static int? ThreadTeacherUserId(WorksheetComment root) =>
        root.AuthorRole == WorksheetCommentAuthorRole.Student ? root.ResponsibleTeacherUserId : root.AuthorUserId;

    /// <summary>issue #305: gizleme/açma yetkisi — admin, worksheet sahibi ya da thread'in sorumlu öğretmeni. Öğrenci asla.</summary>
    private static bool CanModerate(WorksheetComment root, WorksheetCommentActor actor, AccessContext access) =>
        actor.Kind != WorksheetCommentActorKind.Student
        && (access.IsAdmin || access.IsOwner || ThreadTeacherUserId(root) == actor.UserId);

    /// <summary><see cref="ThreadTeacherUserId"/> == uid'in SQL karşılığı (moderatör listesi; sahip/admin dışındaki öğretmen).</summary>
    private static Expression<Func<WorksheetComment, bool>> ModeratedBy(int uid) =>
        c => (c.ParentCommentId == null
                && ((c.AuthorRole == WorksheetCommentAuthorRole.Student && c.ResponsibleTeacherUserId == uid)
                    || (c.AuthorRole == WorksheetCommentAuthorRole.Teacher && c.AuthorUserId == uid)))
            || (c.ParentComment != null
                && ((c.ParentComment.AuthorRole == WorksheetCommentAuthorRole.Student && c.ParentComment.ResponsibleTeacherUserId == uid)
                    || (c.ParentComment.AuthorRole == WorksheetCommentAuthorRole.Teacher && c.ParentComment.AuthorUserId == uid)));

    /// <summary>DTO doldurma bağlamı: moderatör görünümü + istek sahibinin şikayetleri + (moderatöre) şikayet sayıları.</summary>
    private sealed record CommentView(
        WorksheetCommentActor Actor, AccessContext Access, bool ModeratorView,
        IReadOnlySet<int> ReportedByMe, IReadOnlyDictionary<int, int> ReportCounts);

    /// <summary>Sayfadaki yorumlar için iki küçük toplu sorgu: istek sahibinin şikayetleri + moderatör olunanların şikayet sayıları.</summary>
    private async Task<CommentView> BuildViewAsync(WorksheetCommentActor actor, AccessContext access, bool moderatorView,
        IEnumerable<(WorksheetComment Comment, WorksheetComment Root)> items, CancellationToken ct)
    {
        var list = items.ToList();
        var ids = list.Select(x => x.Comment.Id).ToList();
        var reportedByMe = await ReportedByMeAsync(actor, ids, ct);

        var moderated = list.Where(x => CanModerate(x.Root, actor, access)).Select(x => x.Comment.Id).ToList();
        var counts = new Dictionary<int, int>();
        if (moderated.Count > 0)
        {
            counts = await _context.WorksheetCommentReports.AsNoTracking()
                .Where(r => moderated.Contains(r.CommentId))
                .GroupBy(r => r.CommentId)
                .Select(g => new { CommentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.CommentId, x => x.Count, ct);
        }

        return new CommentView(actor, access, moderatorView, reportedByMe, counts);
    }

    private async Task<IReadOnlySet<int>> ReportedByMeAsync(WorksheetCommentActor actor, List<int> commentIds, CancellationToken ct)
    {
        if (commentIds.Count == 0 || actor.Kind == WorksheetCommentActorKind.AdminReader)
            return new HashSet<int>();

        var userId = actor.UserId;
        var mine = await _context.WorksheetCommentReports.AsNoTracking()
            .Where(r => r.ReporterUserId == userId && commentIds.Contains(r.CommentId))
            .Select(r => r.CommentId)
            .ToListAsync(ct);
        return mine.ToHashSet();
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
        root.HiddenAt == null && actor.Kind switch // issue #305: gizli köke cevap yok
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

    /// <summary>
    /// Sorunun kullanıcıya gösterilen 1 tabanlı numarası (issue #309, <see cref="WorksheetQuestionNumbering"/>); soru bu
    /// worksheet'te değilse null (eski <c>IsWorksheetQuestionAsync</c> kontrolünün yerini alır — tek sorgu).
    /// </summary>
    private Task<int?> ResolveQuestionOrderAsync(int worksheetId, int questionId, CancellationToken ct) =>
        WorksheetQuestionNumbering.ResolveNumberAsync(_context, worksheetId, questionId, ct);

    /// <summary>
    /// Öğretmen/admin için öğrencilerin efektif yorum durumu özeti (issue #309): worksheet varsayılanı + çağıranın
    /// görebildiği AKTİF atamalardaki override sayıları. Yalnız admin worksheet'in tüm aktif atamalarını sayar; sahip DAHİL
    /// her öğretmen yalnız KENDİ oluşturduğu aktif atamaları görür (başka öğretmenin atama kararları sızmaz, security L1).
    /// Tek GROUP BY sorgusu; PII yok.
    /// </summary>
    private async Task<WorksheetCommentStudentSummaryDto> BuildStudentCommentsSummaryAsync(
        Worksheet worksheet, WorksheetCommentActor actor, CancellationToken ct)
    {
        var assignments = _context.WorksheetAssignments
            .AsNoTracking()
            .Where(a => a.WorksheetId == worksheet.Id)
            .Where(WorksheetAccess.ActiveAt(DateTime.UtcNow));
        if (!actor.IsAdmin)
        {
            var userId = actor.UserId;
            assignments = assignments.Where(a => a.CreateUserId == userId);
        }

        var counts = await assignments
            .Where(a => a.CommentsEnabledOverride != null)
            .GroupBy(a => a.CommentsEnabledOverride!.Value)
            .Select(g => new { Enabled = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new WorksheetCommentStudentSummaryDto
        {
            WorksheetDefault = worksheet.CommentsEnabled,
            AssignmentOverrides = new WorksheetCommentOverrideCountsDto
            {
                Enabled = counts.Where(c => c.Enabled).Sum(c => c.Count),
                Disabled = counts.Where(c => !c.Enabled).Sum(c => c.Count)
            }
        };
    }

    /// <param name="root">Yorumun kökü (kökse kendisi) — moderatör yetkisi thread'in sorumlu öğretmeninden gelir.</param>
    private WorksheetCommentDto Fill(WorksheetCommentDto target, WorksheetComment source, WorksheetComment root, CommentView view,
        IReadOnlyDictionary<int, string?> fullNames, int? questionOrder)
    {
        var actor = view.Actor;
        fullNames.TryGetValue(source.AuthorUserId, out var fullName);
        target.Id = source.Id;
        target.WorksheetId = source.WorksheetId;
        target.QuestionId = source.QuestionId;
        target.QuestionOrder = source.QuestionId.HasValue ? questionOrder : null;
        target.ParentCommentId = source.ParentCommentId;
        target.AuthorRole = source.AuthorRole;
        target.IsMine = source.AuthorUserId == actor.UserId;
        target.CreatedAt = DateTime.SpecifyKind(source.CreateTime, DateTimeKind.Utc);

        // issue #305: gizli yorum "kaldırıldı" yer tutucusu — gövde ve ad yalnız moderatör görünümünde.
        var canModerate = CanModerate(root, actor, view.Access);
        var hidden = source.HiddenAt != null;
        var reveal = !hidden || (canModerate && view.ModeratorView);
        target.IsHidden = hidden;
        target.Body = reveal ? source.Body : null;
        target.AuthorDisplayName = reveal
            ? DisplayName(source.AuthorRole, fullName)
            : _localizer["worksheets.comments.hiddenAuthor"].Value;
        target.HiddenReason = hidden && reveal ? source.HiddenReason : null;
        target.HiddenAt = hidden && reveal ? DateTime.SpecifyKind(source.HiddenAt!.Value, DateTimeKind.Utc) : null;
        target.CanModerate = canModerate;
        target.ReportCount = canModerate ? view.ReportCounts.GetValueOrDefault(source.Id) : null;
        target.ReportedByMe = view.ReportedByMe.Contains(source.Id);
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
