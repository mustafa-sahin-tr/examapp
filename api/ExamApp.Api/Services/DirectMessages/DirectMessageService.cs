using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.DirectMessages;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.DirectMessages;

/// <inheritdoc cref="IDirectMessageService"/>
public sealed class DirectMessageService : IDirectMessageService
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
    private const int SearchMaxLength = 100;

    private readonly AppDbContext _context;
    private readonly IDirectMessagePolicy _policy;
    private readonly IAuthApiClient _authApiClient;
    private readonly TimeProvider _time;
    private readonly ILogger<DirectMessageService>? _logger;
    private readonly IStringLocalizer<Messages> _localizer;
    private readonly DirectMessageQuotaOptions _quotas;

    /// <summary>Ad çözümü (auth-api) üst süresi; aşılırsa yerelleştirilmiş yedek ad. Liste yine döner.</summary>
    internal TimeSpan NameLookupTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public DirectMessageService(
        AppDbContext context,
        IDirectMessagePolicy policy,
        IAuthApiClient authApiClient,
        TimeProvider? time = null,
        ILogger<DirectMessageService>? logger = null,
        IStringLocalizer<Messages>? localizer = null,
        IOptions<DirectMessageQuotaOptions>? quotas = null)
    {
        _quotas = quotas?.Value ?? new DirectMessageQuotaOptions();
        _context = context;
        _policy = policy;
        _authApiClient = authApiClient;
        _time = time ?? TimeProvider.System;
        _logger = logger;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    // ---- Öğrenci: mesajlaşılabilir öğretmenler ----------------------------------------------------------------

    public async Task<MessageableTeacherPageResultDto> GetMessageableTeachersAsync(
        DirectMessageActor actor, MessageableTeacherQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        if (actor.Kind != DirectMessageActorKind.Student)
            return Fail<MessageableTeacherPageResultDto>(DirectMessageErrorCodes.StudentProfileNotFound, forbidden: true);

        var student = await _policy.ResolveStudentAsync(actor.UserId, ct);
        if (student == null)
            return Fail<MessageableTeacherPageResultDto>(DirectMessageErrorCodes.StudentProfileNotFound, forbidden: true);

        var (page, pageSize) = NormalizePaging(query);
        var ordered = _policy.RelatedTeachers(student, Now, excludeBlocked: true)
            .OrderByDescending(r => r.ViaAssignment)
            .ThenBy(r => r.TeacherId);

        var search = NormalizeSearch(query.Search);
        if (search is { Length: < DirectMessageLimits.MinSearchLength })
            return Fail<MessageableTeacherPageResultDto>(DirectMessageErrorCodes.SearchTooShort);

        List<RelatedTeacherRow> pageRows;
        int total;
        var truncated = false;
        IReadOnlyDictionary<int, UserLookupResultDto> users;

        if (search == null)
        {
            total = await ordered.CountAsync(ct);
            pageRows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            users = await LookupUsersAsync(pageRows.Select(r => r.TeacherUserId), ct) ?? EmptyUsers;
        }
        else
        {
            // Adlar auth-api'de: aday küme (üst sınırlı) çekilir, adlar tek batch'te çözülür, süzme bellekte. Sınır aşılırsa
            // truncated=true (sonuç eksik olabilir). Ad çözümü yoksa arama yapılamaz → 503 (sessizce boş sonuç dönülmez).
            var candidates = await ordered.Take(DirectMessageLimits.MaxSearchCandidates + 1).ToListAsync(ct);
            truncated = candidates.Count > DirectMessageLimits.MaxSearchCandidates;
            if (truncated)
                candidates = candidates.Take(DirectMessageLimits.MaxSearchCandidates).ToList();
            var resolved = await LookupUsersAsync(candidates.Select(r => r.TeacherUserId), ct);
            // Fail-soft istemci (servis token'ı alınamadı) boş liste döner: aday varken hiç ad gelmemesi de "kullanılamıyor".
            if (resolved == null || (candidates.Count > 0 && resolved.Count == 0))
                return Fail<MessageableTeacherPageResultDto>(DirectMessageErrorCodes.NameLookupUnavailable, serviceUnavailable: true);
            users = resolved;
            var matches = candidates
                .Where(r => users.TryGetValue(r.TeacherUserId, out var u)
                            && !string.IsNullOrWhiteSpace(u.FullName)
                            && TurkishCulture.CompareInfo.IndexOf(u.FullName, search, CompareOptions.IgnoreCase) >= 0)
                .ToList();
            total = matches.Count;
            pageRows = matches.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        var teacherUserIds = pageRows.Select(r => r.TeacherUserId).ToList();
        var studentUserId = student.UserId;
        var conversations = teacherUserIds.Count == 0
            ? new Dictionary<int, int>()
            : await _context.Conversations.AsNoTracking()
                .Where(c => c.StudentUserId == studentUserId && teacherUserIds.Contains(c.TeacherUserId))
                .Select(c => new { c.TeacherUserId, c.Id })
                .ToDictionaryAsync(c => c.TeacherUserId, c => c.Id, ct);

        var items = pageRows.Select(r =>
        {
            users.TryGetValue(r.TeacherUserId, out var user);
            return new MessageableTeacherDto
            {
                TeacherId = r.TeacherId,
                FullName = TeacherName(user, r.TeacherId),
                Avatar = user?.Avatar ?? string.Empty,
                Relation = DirectMessageRelations.For(r.ViaSchool, r.ViaAssignment),
                ConversationId = conversations.TryGetValue(r.TeacherUserId, out var cid) ? cid : null
            };
        }).ToList();

        return new MessageableTeacherPageResultDto
        {
            Success = true,
            Page = new MessageableTeacherPageDto { Items = items, Page = page, PageSize = pageSize, TotalCount = total, Truncated = truncated }
        };
    }

    // ---- Gönderme ---------------------------------------------------------------------------------------------

    public async Task<SendDirectMessageResultDto> SendToTeacherAsync(
        DirectMessageActor actor, int teacherId, SendDirectMessageDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(dto);

        // Öğretmen yeni konuşma başlatamaz (ürün kararı); bu uç yalnız öğrenciye açık.
        if (actor.Kind != DirectMessageActorKind.Student)
            return Fail<SendDirectMessageResultDto>(DirectMessageErrorCodes.CannotMessageTeacher, forbidden: true);

        var student = await _policy.ResolveStudentAsync(actor.UserId, ct);
        if (student == null)
            return Fail<SendDirectMessageResultDto>(DirectMessageErrorCodes.StudentProfileNotFound, forbidden: true);

        // Öğretmen yoksa da aynı nötr 403 — Teachers.Id'nin varlığı sızdırılmaz.
        var teacherUserId = await _context.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => (int?)t.UserId)
            .FirstOrDefaultAsync(ct);
        if (teacherUserId == null || !await _policy.CanMessageAsync(student, teacherUserId.Value, ct))
            return Fail<SendDirectMessageResultDto>(DirectMessageErrorCodes.CannotMessageTeacher, forbidden: true);

        if (SanitizeBody(dto.Body, out var body) is { } invalid)
            return Fail<SendDirectMessageResultDto>(invalid);

        return await AppendAsync(actor, student.UserId, teacherUserId.Value, body, ct);
    }

    public async Task<SendDirectMessageResultDto> SendToConversationAsync(
        DirectMessageActor actor, int conversationId, SendDirectMessageDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(dto);

        var conversation = await LoadConversationAsync(conversationId, ct);
        if (conversation == null)
            return Fail<SendDirectMessageResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);
        if (!IsParty(conversation, actor))
            return Fail<SendDirectMessageResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);

        var student = await _policy.ResolveStudentAsync(conversation.StudentUserId, ct);
        if (actor.Kind == DirectMessageActorKind.Student)
        {
            if (student == null || !await _policy.CanMessageAsync(student, conversation.TeacherUserId, ct))
                return Fail<SendDirectMessageResultDto>(DirectMessageErrorCodes.CannotMessageTeacher, forbidden: true);
        }
        else if (student == null || !await _policy.HasActiveRelationAsync(student, conversation.TeacherUserId, ct))
        {
            // Öğretmen cevabı engelde serbest; ilişki (okul/atama) bittiyse kapalı — bkz. rapor gerekçesi (reşit olmayan
            // öğrenciyle ilişkisi kalmamış yetişkin yazışmayı sürdüremez; konuşma okunur kalır).
            return Fail<SendDirectMessageResultDto>(DirectMessageErrorCodes.RelationshipEnded, forbidden: true);
        }

        if (SanitizeBody(dto.Body, out var body) is { } invalid)
            return Fail<SendDirectMessageResultDto>(invalid);

        return await AppendAsync(actor, conversation.StudentUserId, conversation.TeacherUserId, body, ct);
    }

    /// <summary>
    /// Mesajı (gerekirse konuşmayla birlikte) TEK SaveChanges'ta yazar. Eşzamanlı ilk mesajda ikinci konuşma insert'i tekil
    /// index'e takılır: eklenenler ayrılır, kazanan konuşma okunur ve mesaj ona yazılır (çift başına tek konuşma).
    /// </summary>
    private async Task<SendDirectMessageResultDto> AppendAsync(
        DirectMessageActor actor, int studentUserId, int teacherUserId, string body, CancellationToken ct)
    {
        _context.SetCurrentUser(actor.UserId);
        var role = actor.Kind == DirectMessageActorKind.Student ? DirectMessageSenderRole.Student : DirectMessageSenderRole.Teacher;

        for (var attempt = 0; ; attempt++)
        {
            var now = Now;
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.StudentUserId == studentUserId && c.TeacherUserId == teacherUserId, ct);
            var created = conversation == null;
            if (created)
            {
                // Konuşmayı yalnız öğrenci açar (öğretmen bu yola ancak mevcut konuşmayla gelir).
                if (actor.Kind != DirectMessageActorKind.Student)
                    return Fail<SendDirectMessageResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);
                conversation = new Conversation { StudentUserId = studentUserId, TeacherUserId = teacherUserId, LastMessageAt = now };
                _context.Conversations.Add(conversation);
            }
            else
            {
                conversation!.LastMessageAt = now;
            }

            // Kotalar (security O2): öğrenci başına günlük yeni konuşma, gönderen başına konuşma içi saatlik mesaj.
            if (await CheckQuotaAsync(actor.UserId, created ? null : conversation.Id, now, ct) is { } retryAfter)
            {
                _context.ChangeTracker.Clear();
                return Fail<SendDirectMessageResultDto>(DirectMessageErrorCodes.RateLimited, retryAfterSeconds: retryAfter);
            }

            var message = new DirectMessage
            {
                Conversation = conversation,
                SenderUserId = actor.UserId,
                SenderRole = role,
                Body = body
            };
            _context.DirectMessages.Add(message);

            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (created && attempt == 0 && DbUpdateExceptionClassifier.IsUniqueViolation(ex))
            {
                // Yarış: başka istek aynı çiftin konuşmasını önce açtı. Ekleneni bırak, kazananla bir kez daha dene.
                _context.ChangeTracker.Clear();
                continue;
            }

            return new SendDirectMessageResultDto
            {
                Success = true,
                ObjectId = message.Id,
                ConversationId = conversation.Id,
                ConversationCreated = created,
                Message = _localizer["directMessages.sent"],
                DirectMessage = ToDto(message, conversation.Id, actor.UserId)
            };
        }
    }

    /// <summary>
    /// Kota aşıldıysa Retry-After (saniye), değilse null. Yeni konuşma: son 24 saatte öğrencinin açtığı konuşma sayısı;
    /// mevcut konuşma: gönderenin bu konuşmada son 1 saatteki mesaj sayısı. DB'den sayılır (dağıtık, restart'a dayanıklı);
    /// eşzamanlı isteklerde sınır birkaç mesaj aşılabilir (yaklaşık kota, HTTP kovası ayrıca var).
    /// </summary>
    private async Task<int?> CheckQuotaAsync(int senderUserId, int? conversationId, DateTime now, CancellationToken ct)
    {
        if (conversationId is not int cid)
        {
            var since = now.AddDays(-1);
            var recent = await _context.Conversations.AsNoTracking()
                .Where(c => c.StudentUserId == senderUserId && c.CreateTime > since)
                .OrderBy(c => c.CreateTime)
                .Select(c => c.CreateTime)
                .ToListAsync(ct);
            return recent.Count >= _quotas.NewConversationsPerDay
                ? RetryAfter(recent[recent.Count - _quotas.NewConversationsPerDay], TimeSpan.FromDays(1), now)
                : null;
        }

        var hourAgo = now.AddHours(-1);
        var sent = await _context.DirectMessages.AsNoTracking()
            .Where(m => m.ConversationId == cid && m.SenderUserId == senderUserId && m.CreateTime > hourAgo)
            .OrderBy(m => m.CreateTime)
            .Select(m => m.CreateTime)
            .ToListAsync(ct);
        return sent.Count >= _quotas.MessagesPerConversationPerHour
            ? RetryAfter(sent[sent.Count - _quotas.MessagesPerConversationPerHour], TimeSpan.FromHours(1), now)
            : null;

        static int RetryAfter(DateTime oldestCounted, TimeSpan window, DateTime now)
            => Math.Max(1, (int)Math.Ceiling((oldestCounted + window - now).TotalSeconds));
    }

    // ---- Okundu -----------------------------------------------------------------------------------------------

    public async Task<MarkDirectMessagesReadResultDto> MarkReadAsync(
        DirectMessageActor actor, int conversationId, MarkDirectMessagesReadDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(dto);

        var conversation = await LoadConversationAsync(conversationId, ct);
        if (conversation == null || !IsParty(conversation, actor))
            return Fail<MarkDirectMessagesReadResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);
        if (dto.UpToMessageId is not int upTo || upTo <= 0)
            return Fail<MarkDirectMessagesReadResultDto>(DirectMessageErrorCodes.InvalidUpToMessageId);

        // Yalnız karşı tarafın, Id <= upTo olan okunmamış mesajları; ReadAt + UpdateTime/UpdateUserId (audit alanları).
        var me = actor.UserId;
        var now = Now;
        var marked = await _context.DirectMessages
            .Where(m => m.ConversationId == conversationId && m.Id <= upTo && m.SenderUserId != me && m.ReadAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.ReadAt, (DateTime?)now)
                .SetProperty(m => m.UpdateTime, (DateTime?)now)
                .SetProperty(m => m.UpdateUserId, (int?)me), ct);

        return new MarkDirectMessagesReadResultDto
        {
            Success = true,
            ObjectId = conversationId,
            ConversationId = conversationId,
            MarkedCount = marked
        };
    }

    // ---- Listeler ---------------------------------------------------------------------------------------------

    public async Task<ConversationPageResultDto> GetStudentConversationsAsync(
        DirectMessageActor actor, DirectMessagePageQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);
        if (actor.Kind != DirectMessageActorKind.Student)
            return Fail<ConversationPageResultDto>(DirectMessageErrorCodes.StudentProfileNotFound, forbidden: true);

        var me = actor.UserId;
        var (page, pageSize) = NormalizePaging(query);
        var baseQuery = _context.Conversations.AsNoTracking().Where(c => c.StudentUserId == me);
        var total = await baseQuery.CountAsync(ct);
        var rows = await ProjectSummaries(baseQuery, me, page, pageSize).ToListAsync(ct);

        var users = await LookupUsersAsync(rows.Select(r => r.TeacherUserId), ct) ?? EmptyUsers;
        var items = rows.Select(r =>
        {
            users.TryGetValue(r.TeacherUserId, out var user);
            var dto = ToSummary(r, me);
            dto.CounterpartName = TeacherName(user, r.TeacherId ?? 0);
            dto.CounterpartAvatar = user?.Avatar ?? string.Empty;
            dto.TeacherId = r.TeacherId;
            return dto;
        }).ToList();

        return new ConversationPageResultDto
        {
            Success = true,
            Page = new ConversationPageDto { Items = items, Page = page, PageSize = pageSize, TotalCount = total }
        };
    }

    public async Task<ConversationPageResultDto> GetTeacherInboxAsync(
        DirectMessageActor actor, TeacherInboxQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);
        if (actor.Kind != DirectMessageActorKind.Teacher)
            return Fail<ConversationPageResultDto>(DirectMessageErrorCodes.TeacherProfileNotFound, forbidden: true);

        var me = actor.UserId;
        var (page, pageSize) = NormalizePaging(query);
        var baseQuery = _context.Conversations.AsNoTracking().Where(c => c.TeacherUserId == me);

        switch (string.IsNullOrWhiteSpace(query.Filter) ? TeacherInboxFilters.All : query.Filter.Trim().ToLowerInvariant())
        {
            case TeacherInboxFilters.All:
                break;
            case TeacherInboxFilters.Unread:
                baseQuery = baseQuery.Where(c => _context.DirectMessages
                    .Any(m => m.ConversationId == c.Id && m.SenderUserId != me && m.ReadAt == null));
                break;
            case TeacherInboxFilters.Blocked:
                baseQuery = baseQuery.Where(c => _context.DirectMessageBlocks
                    .Any(b => b.TeacherUserId == me && b.StudentUserId == c.StudentUserId));
                break;
            default:
                return Fail<ConversationPageResultDto>(DirectMessageErrorCodes.InvalidFilter);
        }

        var total = await baseQuery.CountAsync(ct);
        var rows = await ProjectSummaries(baseQuery, me, page, pageSize).ToListAsync(ct);

        var users = await LookupUsersAsync(rows.Select(r => r.StudentUserId), ct) ?? EmptyUsers;
        var items = rows.Select(r =>
        {
            users.TryGetValue(r.StudentUserId, out var user);
            var dto = ToSummary(r, me);
            dto.CounterpartName = StudentName(user, r.StudentId ?? 0);
            dto.CounterpartAvatar = user?.Avatar ?? string.Empty;
            dto.StudentId = r.StudentId;
            dto.IsBlocked = r.Blocked;
            return dto;
        }).ToList();

        return new ConversationPageResultDto
        {
            Success = true,
            Page = new ConversationPageDto { Items = items, Page = page, PageSize = pageSize, TotalCount = total }
        };
    }

    private sealed class SummaryRow
    {
        public int Id { get; init; }
        public int StudentUserId { get; init; }
        public int TeacherUserId { get; init; }
        public DateTime LastMessageAt { get; init; }
        public string? LastBody { get; init; }
        public int? LastSenderUserId { get; init; }
        public int Unread { get; init; }
        public bool Blocked { get; init; }
        public int? TeacherId { get; init; }
        public int? StudentId { get; init; }
    }

    /// <summary>Tek sorgu: son mesaj / okunmamış / engel / canlı profil Id'leri skaler alt sorgular (APPLY yok, N+1 yok).</summary>
    private IQueryable<SummaryRow> ProjectSummaries(IQueryable<Conversation> query, int me, int page, int pageSize)
        => query
            .OrderByDescending(c => c.LastMessageAt)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new SummaryRow
            {
                Id = c.Id,
                StudentUserId = c.StudentUserId,
                TeacherUserId = c.TeacherUserId,
                LastMessageAt = c.LastMessageAt,
                LastBody = _context.DirectMessages.Where(m => m.ConversationId == c.Id)
                    .OrderByDescending(m => m.Id).Select(m => m.Body).FirstOrDefault(),
                LastSenderUserId = _context.DirectMessages.Where(m => m.ConversationId == c.Id)
                    .OrderByDescending(m => m.Id).Select(m => (int?)m.SenderUserId).FirstOrDefault(),
                Unread = _context.DirectMessages.Count(m => m.ConversationId == c.Id && m.SenderUserId != me && m.ReadAt == null),
                Blocked = _context.DirectMessageBlocks.Any(b => b.TeacherUserId == c.TeacherUserId && b.StudentUserId == c.StudentUserId),
                TeacherId = _context.Teachers.Where(t => t.UserId == c.TeacherUserId).OrderBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefault(),
                StudentId = _context.Students.Where(s => s.UserId == c.StudentUserId).OrderBy(s => s.Id).Select(s => (int?)s.Id).FirstOrDefault()
            });

    private static ConversationSummaryDto ToSummary(SummaryRow r, int me) => new()
    {
        ConversationId = r.Id,
        LastMessageAt = r.LastMessageAt,
        LastMessagePreview = Preview(r.LastBody),
        LastMessageIsMine = r.LastSenderUserId == me,
        UnreadCount = r.Unread
    };

    // ---- Konuşma mesajları ------------------------------------------------------------------------------------

    public async Task<ConversationMessagesResultDto> GetMessagesAsync(
        DirectMessageActor actor, int conversationId, DirectMessageHistoryQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);

        var conversation = await LoadConversationAsync(conversationId, ct);
        if (conversation == null)
            return Fail<ConversationMessagesResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);
        if (!IsParty(conversation, actor))
            return Fail<ConversationMessagesResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);

        var me = actor.UserId;
        var take = Math.Clamp(query.Take, 1, DirectMessageLimits.MaxMessageTake);

        // Okuma yan etkisizdir (code review W2): okundu işaretleme ayrı uçta (MarkReadAsync, upToMessageId'ye kadar).
        var messagesQuery = _context.DirectMessages.AsNoTracking().Where(m => m.ConversationId == conversationId);
        if (query.BeforeId is int beforeId)
            messagesQuery = messagesQuery.Where(m => m.Id < beforeId);

        var newestFirst = await messagesQuery.OrderByDescending(m => m.Id).Take(take + 1).ToListAsync(ct);
        var hasMore = newestFirst.Count > take;
        var pageItems = newestFirst.Take(take).OrderBy(m => m.Id).ToList();

        var student = await _policy.ResolveStudentAsync(conversation.StudentUserId, ct);
        bool canSend;
        bool? isBlocked = null;
        if (actor.Kind == DirectMessageActorKind.Student)
        {
            canSend = student != null && await _policy.CanMessageAsync(student, conversation.TeacherUserId, ct);
        }
        else
        {
            canSend = student != null && await _policy.HasActiveRelationAsync(student, conversation.TeacherUserId, ct);
            isBlocked = await _context.DirectMessageBlocks.AsNoTracking()
                .AnyAsync(b => b.TeacherUserId == conversation.TeacherUserId && b.StudentUserId == conversation.StudentUserId, ct);
        }

        var counterpartUserId = actor.Kind == DirectMessageActorKind.Student ? conversation.TeacherUserId : conversation.StudentUserId;
        var users = await LookupUsersAsync(new[] { counterpartUserId }, ct) ?? EmptyUsers;
        users.TryGetValue(counterpartUserId, out var counterpart);

        int? teacherId = null;
        int? studentId = null;
        string name;
        if (actor.Kind == DirectMessageActorKind.Student)
        {
            teacherId = await _context.Teachers.AsNoTracking().Where(t => t.UserId == conversation.TeacherUserId)
                .OrderBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
            name = TeacherName(counterpart, teacherId ?? 0);
        }
        else
        {
            studentId = student?.StudentId;
            name = StudentName(counterpart, studentId ?? 0);
        }

        return new ConversationMessagesResultDto
        {
            Success = true,
            ObjectId = conversationId,
            Conversation = new ConversationMessagesDto
            {
                ConversationId = conversationId,
                CounterpartName = name,
                CounterpartAvatar = counterpart?.Avatar ?? string.Empty,
                TeacherId = teacherId,
                StudentId = studentId,
                CanSend = canSend,
                IsBlocked = isBlocked,
                Items = pageItems.Select(m => ToDto(m, conversationId, me)).ToList(),
                HasMore = hasMore,
                NextBeforeId = hasMore ? pageItems.FirstOrDefault()?.Id : null
            }
        };
    }

    // ---- Engel ------------------------------------------------------------------------------------------------

    public async Task<DirectMessageBlockResultDto> SetBlockedAsync(
        DirectMessageActor actor, int conversationId, bool blocked, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var conversation = await LoadConversationAsync(conversationId, ct);
        if (conversation == null)
            return Fail<DirectMessageBlockResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);
        // Taraf değilse 404 (varlık sızdırılmaz, security D2); taraf olan öğrenci engel koyamaz (403). Yalnız konuşmanın öğretmeni.
        if (!IsParty(conversation, actor))
            return Fail<DirectMessageBlockResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);
        if (actor.Kind != DirectMessageActorKind.Teacher)
            return Fail<DirectMessageBlockResultDto>(DirectMessageErrorCodes.BlockTeacherOnly, forbidden: true);

        var teacherUserId = conversation.TeacherUserId;
        var studentUserId = conversation.StudentUserId;
        _context.SetCurrentUser(actor.UserId);

        var active = await _context.DirectMessageBlocks
            .FirstOrDefaultAsync(b => b.TeacherUserId == teacherUserId && b.StudentUserId == studentUserId, ct);

        var changed = false;
        if (blocked && active == null)
        {
            var block = new DirectMessageBlock { TeacherUserId = teacherUserId, StudentUserId = studentUserId };
            _context.DirectMessageBlocks.Add(block);
            AddAudit(actor, AdminUserAction.DirectMessageStudentBlocked, conversationId);
            try
            {
                await _context.SaveChangesAsync(ct); // engel + audit tek SaveChanges (atomik)
                changed = true;
            }
            catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
            {
                // Eşzamanlı ikinci engel isteği tekil index'e takıldı: engel zaten var → idempotent.
                _context.ChangeTracker.Clear();
                if (!await _context.DirectMessageBlocks.AsNoTracking()
                        .AnyAsync(b => b.TeacherUserId == teacherUserId && b.StudentUserId == studentUserId, ct))
                    throw;
            }
        }
        else if (!blocked && active != null)
        {
            // Koşullu soft-delete (code review Ö2): yalnız hâlâ aktif satır kaldırılır; audit yalnız gerçekten 1 satır değiştiyse.
            // Eşzamanlı iki kaldırma isteğinde ikincisi 0 satır günceller → changed=false, ikinci audit yok. UPDATE + audit tek
            // transaction'da, execution strategy içinde (retry güvenli: her denemede iz temizlenir).
            var activeId = active.Id;
            var actorUserId = actor.UserId;
            var strategy = _context.Database.CreateExecutionStrategy();
            changed = await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var tx = await _context.Database.BeginTransactionAsync(ct);
                var now = Now;
                var affected = await _context.DirectMessageBlocks
                    .Where(b => b.Id == activeId && !b.IsDeleted)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.IsDeleted, true)
                        .SetProperty(b => b.DeleteTime, (DateTime?)now)
                        .SetProperty(b => b.DeleteUserId, (int?)actorUserId), ct);
                if (affected == 1)
                {
                    AddAudit(actor, AdminUserAction.DirectMessageStudentUnblocked, conversationId);
                    await _context.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct);
                return affected == 1;
            });
        }

        return new DirectMessageBlockResultDto
        {
            Success = true,
            ObjectId = conversationId,
            ConversationId = conversationId,
            IsBlocked = blocked,
            Changed = changed,
            Message = _localizer[blocked ? "directMessages.blocked" : "directMessages.unblocked"]
        };
    }

    private void AddAudit(DirectMessageActor actor, AdminUserAction action, int conversationId)
        => _context.AdminUserActionLogs.Add(new AdminUserActionLog
        {
            ActorKeycloakId = actor.KeycloakId,
            Action = action,
            TargetType = AdminUserTargetType.Conversation,
            TargetId = conversationId,
            Outcome = AdminUserActionOutcome.Succeeded,
            OccurredAtUtc = Now
        });

    // ---- Şikayet ----------------------------------------------------------------------------------------------

    public async Task<DirectMessageReportResultDto> ReportAsync(
        DirectMessageActor actor, int conversationId, ReportDirectMessageDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(dto);

        var conversation = await LoadConversationAsync(conversationId, ct);
        if (conversation == null)
            return Fail<DirectMessageReportResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);
        if (!IsParty(conversation, actor))
            return Fail<DirectMessageReportResultDto>(DirectMessageErrorCodes.ConversationNotFound, notFound: true);

        if (!DirectMessageReportReasons.TryParse(dto.Reason, out var reason))
            return Fail<DirectMessageReportResultDto>(DirectMessageErrorCodes.InvalidReportReason);

        string? note = null;
        if (!string.IsNullOrWhiteSpace(dto.Note))
        {
            switch (CommentBodySanitizer.TrySanitize(dto.Note, DirectMessageReport.NoteMaxLength, out var cleaned))
            {
                case CommentBodySanitizer.Outcome.Ok:
                    note = cleaned;
                    break;
                case CommentBodySanitizer.Outcome.InvalidCharacters:
                    return Fail<DirectMessageReportResultDto>(DirectMessageErrorCodes.ReportNoteInvalidCharacters);
                case CommentBodySanitizer.Outcome.TooLong:
                    return Fail<DirectMessageReportResultDto>(DirectMessageErrorCodes.ReportNoteTooLong);
                // Required: yalnız görünmez karakterlerden oluşan not → notsuz şikayet.
            }
        }

        var messageId = dto.MessageId;
        if (messageId is int mid)
        {
            var sender = await _context.DirectMessages.AsNoTracking()
                .Where(m => m.Id == mid && m.ConversationId == conversationId)
                .Select(m => (int?)m.SenderUserId)
                .FirstOrDefaultAsync(ct);
            if (sender == null)
                return Fail<DirectMessageReportResultDto>(DirectMessageErrorCodes.MessageNotFound, notFound: true);
            if (sender == actor.UserId)
                return Fail<DirectMessageReportResultDto>(DirectMessageErrorCodes.CannotReportOwnMessage, forbidden: true);
        }

        var existing = await FindReportIdAsync(conversationId, messageId, actor.UserId, ct);
        if (existing is int existingId)
            return Reported(existingId, alreadyReported: true);

        _context.SetCurrentUser(actor.UserId);
        var report = new DirectMessageReport
        {
            ConversationId = conversationId,
            MessageId = messageId,
            ReporterUserId = actor.UserId,
            ReporterRole = actor.Kind == DirectMessageActorKind.Student ? DirectMessageSenderRole.Student : DirectMessageSenderRole.Teacher,
            Reason = reason,
            Note = note,
            Status = DirectMessageReportStatus.Open
        };
        _context.DirectMessageReports.Add(report);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbUpdateExceptionClassifier.IsUniqueViolation(ex))
        {
            // Eşzamanlı ikinci şikayet tekil index'e takıldı: ilk kayıt duruyorsa idempotent yanıt, değilse gerçek hata.
            _context.ChangeTracker.Clear();
            if (await FindReportIdAsync(conversationId, messageId, actor.UserId, ct) is int raced)
                return Reported(raced, alreadyReported: true);
            throw;
        }

        return Reported(report.Id, alreadyReported: false);
    }

    private Task<int?> FindReportIdAsync(int conversationId, int? messageId, int reporterUserId, CancellationToken ct)
    {
        var query = _context.DirectMessageReports.AsNoTracking().Where(r => r.ReporterUserId == reporterUserId);
        query = messageId is int mid
            ? query.Where(r => r.MessageId == mid)
            : query.Where(r => r.ConversationId == conversationId && r.MessageId == null);
        return query.OrderBy(r => r.Id).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
    }

    private DirectMessageReportResultDto Reported(int reportId, bool alreadyReported) => new()
    {
        Success = true,
        ObjectId = reportId,
        ReportId = reportId,
        AlreadyReported = alreadyReported,
        Message = _localizer[alreadyReported ? "directMessages.alreadyReported" : "directMessages.reported"]
    };

    public async Task<DirectMessageReportPageResultDto> GetOpenReportsAsync(DirectMessagePageQueryDto query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, pageSize) = NormalizePaging(query);

        var open = _context.DirectMessageReports.AsNoTracking().Where(r => r.Status == DirectMessageReportStatus.Open);
        var total = await open.CountAsync(ct);
        var rows = await open
            .OrderByDescending(r => r.CreateTime)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.ConversationId,
                r.MessageId,
                r.Reason,
                r.Note,
                r.Status,
                r.CreateTime,
                r.ReporterRole,
                r.ReporterUserId,
                StudentUserId = _context.Conversations.Where(c => c.Id == r.ConversationId).Select(c => c.StudentUserId).FirstOrDefault(),
                TeacherUserId = _context.Conversations.Where(c => c.Id == r.ConversationId).Select(c => c.TeacherUserId).FirstOrDefault(),
                MessageBody = _context.DirectMessages.Where(m => m.Id == r.MessageId).Select(m => m.Body).FirstOrDefault(),
                MessageSenderRole = _context.DirectMessages.Where(m => m.Id == r.MessageId).Select(m => (DirectMessageSenderRole?)m.SenderRole).FirstOrDefault(),
                MessageSentAt = _context.DirectMessages.Where(m => m.Id == r.MessageId).Select(m => (DateTime?)m.CreateTime).FirstOrDefault()
            })
            .ToListAsync(ct);

        var users = await LookupUsersAsync(rows.SelectMany(r => new[] { r.StudentUserId, r.TeacherUserId }), ct) ?? EmptyUsers;
        var items = rows.Select(r => new DirectMessageReportItemDto
        {
            ReportId = r.Id,
            ConversationId = r.ConversationId,
            MessageId = r.MessageId,
            Reason = DirectMessageReportReasons.ToJson(r.Reason),
            Note = r.Note,
            Status = r.Status.ToString(),
            ReportedAt = r.CreateTime,
            ReporterRole = r.ReporterRole.ToString(),
            ReporterUserId = r.ReporterUserId,
            StudentUserId = r.StudentUserId,
            StudentName = users.TryGetValue(r.StudentUserId, out var s) && !string.IsNullOrWhiteSpace(s.FullName)
                ? s.FullName : _localizer["teacher.fallbackStudentName", r.StudentUserId],
            TeacherUserId = r.TeacherUserId,
            TeacherName = users.TryGetValue(r.TeacherUserId, out var t) && !string.IsNullOrWhiteSpace(t.FullName)
                ? t.FullName : _localizer["teacher.fallbackTeacherName", r.TeacherUserId],
            MessageBody = r.MessageId == null ? null : r.MessageBody,
            MessageSenderRole = r.MessageId == null ? null : r.MessageSenderRole?.ToString(),
            MessageSentAt = r.MessageId == null ? null : r.MessageSentAt
        }).ToList();

        return new DirectMessageReportPageResultDto
        {
            Success = true,
            Page = new DirectMessageReportPageDto { Items = items, Page = page, PageSize = pageSize, TotalCount = total }
        };
    }

    // ---- Yardımcılar ------------------------------------------------------------------------------------------

    private Task<Conversation?> LoadConversationAsync(int conversationId, CancellationToken ct)
        => _context.Conversations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversationId, ct);

    private static bool IsParty(Conversation conversation, DirectMessageActor actor) => actor.Kind switch
    {
        DirectMessageActorKind.Student => conversation.StudentUserId == actor.UserId,
        DirectMessageActorKind.Teacher => conversation.TeacherUserId == actor.UserId,
        _ => false
    };

    /// <summary>Düz metin temizliği (#105 ile aynı kural): kontrol karakteri → 400, bidi/sıfır genişlik ayıklanır, trim, ≤ 2000.</summary>
    private static string? SanitizeBody(string? raw, out string body)
        => CommentBodySanitizer.TrySanitize(raw, DirectMessage.BodyMaxLength, out body) switch
        {
            CommentBodySanitizer.Outcome.Ok => null,
            CommentBodySanitizer.Outcome.Required => DirectMessageErrorCodes.BodyRequired,
            CommentBodySanitizer.Outcome.TooLong => DirectMessageErrorCodes.BodyTooLong,
            _ => DirectMessageErrorCodes.BodyInvalidCharacters
        };

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;
        var trimmed = search.Trim();
        return trimmed.Length > SearchMaxLength ? trimmed[..SearchMaxLength] : trimmed;
    }

    private static (int Page, int PageSize) NormalizePaging(DirectMessagePageQueryDto query)
        => (Math.Clamp(query.Page, 1, DirectMessageLimits.MaxPage), Math.Clamp(query.PageSize <= 0 ? DirectMessageLimits.DefaultPageSize : query.PageSize, 1, DirectMessageLimits.MaxPageSize));

    private static string Preview(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return string.Empty;
        return body.Length <= DirectMessageLimits.PreviewLength ? body : body[..DirectMessageLimits.PreviewLength].TrimEnd() + "…";
    }

    private static DirectMessageDto ToDto(DirectMessage m, int conversationId, int me) => new()
    {
        Id = m.Id,
        ConversationId = conversationId,
        Body = m.Body,
        SenderRole = m.SenderRole.ToString(),
        IsMine = m.SenderUserId == me,
        SentAt = m.CreateTime
    };

    private string TeacherName(UserLookupResultDto? user, int teacherId)
        => !string.IsNullOrWhiteSpace(user?.FullName) ? user!.FullName : _localizer["teacher.fallbackTeacherName", teacherId];

    private string StudentName(UserLookupResultDto? user, int studentId)
        => !string.IsNullOrWhiteSpace(user?.FullName) ? user!.FullName : _localizer["teacher.fallbackStudentName", studentId];

    private static readonly IReadOnlyDictionary<int, UserLookupResultDto> EmptyUsers = new Dictionary<int, UserLookupResultDto>();

    /// <summary>
    /// Ad/avatar zenginleştirmesi: auth-api erişilemezse ya da süre aşılırsa null (listeler yedek ada düşer; aramada 503).
    /// </summary>
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
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _logger?.LogWarning(ex, "[DirectMessages] Kullanıcı adları çözülemedi ({Count} kullanıcı).", ids.Count);
            return null;
        }
    }

    private T Fail<T>(string code, bool notFound = false, bool forbidden = false, bool serviceUnavailable = false,
        int? retryAfterSeconds = null) where T : DirectMessageResponseDto, new() => new()
    {
        Success = false,
        NotFound = notFound,
        Forbidden = forbidden,
        ServiceUnavailable = serviceUnavailable,
        RateLimited = code == DirectMessageErrorCodes.RateLimited,
        RetryAfterSeconds = retryAfterSeconds,
        ErrorCode = code,
        Message = _localizer["directMessages.errors." + char.ToLowerInvariant(code[0]) + code[1..]]
    };
}
