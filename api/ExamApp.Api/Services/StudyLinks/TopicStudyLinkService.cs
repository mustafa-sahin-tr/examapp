using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.StudyLinks;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.StudyLinks;

/// <summary>
/// Konu / alt konu harici çalışma linkleri (issue #61).
///
/// <para><b>Kapsam anahtarı:</b> SubTopicId doluysa alt konu, değilse konu (TopicId dolu, SubTopicId boş).
/// Alt konu linkinde TopicId alt konunun üst konusuyla doldurulur ama sayım/listeleme alt konu üzerinden yapılır.</para>
///
/// <para><b>7 aktif link limiti ve eşzamanlılık:</b> "say → ekle/aktifleştir" iki ayrı SQL ifadesi olduğu için
/// iki yönetici aynı anda 7. linki eklerse ikisi de 6 görüp 8'e çıkabilirdi (write skew). Bu yüzden sayım ve yazma
/// <see cref="IsolationLevel.Serializable"/> transaction içinde yapılır: PostgreSQL SSI çakışan ikinci transaction'ı
/// serialization_failure (40001) ile düşürür; Npgsql execution strategy'si (Aspire AddNpgsqlDbContext → retry açık)
/// bunu geçici hata sayıp işlemi baştan dener, ikinci denemede sayım 7'yi görür ve limit hatası döner. Retry
/// tükenirse istemciye 409 (concurrentModification) döner. Öğrenci tarafı savunma amaçlı alt konu başına en fazla
/// 7 link gösterir.</para>
/// </summary>
public class TopicStudyLinkService : ITopicStudyLinkService
{
    private readonly AppDbContext _context;
    private readonly ILogger<TopicStudyLinkService> _logger;

    // Client'a ulaşan tüm metinler mesaj sözlüğünden gelir (issue #184). Parametre yalnızca DI'sız
    // (birim test) kurulum için opsiyonel; DI her zaman gerçek localizer'ı verir.
    private readonly IStringLocalizer<Messages> _localizer;

    public TopicStudyLinkService(
        AppDbContext context,
        ILogger<TopicStudyLinkService>? logger = null,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _logger = logger ?? NullLogger<TopicStudyLinkService>.Instance;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    // ---------------- Yönetim (Admin / Teacher) ----------------

    public async Task<TopicStudyLinkListResultDto> ListAsync(TopicStudyLinkQueryDto query, CancellationToken ct = default)
    {
        var scope = await ResolveExactScopeAsync(query.TopicId, query.SubTopicId, ct);
        if (scope.Error != null)
            return Fail<TopicStudyLinkListResultDto>(scope.Error, notFound: scope.NotFound);

        return await BuildListAsync(scope.Scope, query.IncludeInactive, query.Skip, query.Take, ct);
    }

    public async Task<TopicStudyLinkResultDto> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var link = await _context.TopicStudyLinks.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(ToDtoExpression)
            .FirstOrDefaultAsync(ct);

        return link == null
            ? Fail<TopicStudyLinkResultDto>("studyLinks.notFound", notFound: true)
            : new TopicStudyLinkResultDto { Success = true, ObjectId = link.Id, Link = link };
    }

    public async Task<TopicStudyLinkResultDto> CreateAsync(CreateTopicStudyLinkDto dto, UserProfileDto user, CancellationToken ct = default)
    {
        var fields = ValidateFields(dto.Title, dto.Url, dto.SourceType);
        if (fields.Error != null)
            return Fail<TopicStudyLinkResultDto>(fields.Error);

        var scope = await ResolveCreateScopeAsync(dto.TopicId, dto.SubTopicId, ct);
        if (scope.Error != null)
            return Fail<TopicStudyLinkResultDto>(scope.Error);

        return await RunSerializableAsync<TopicStudyLinkResultDto>(async () =>
        {
            if (dto.IsActive && await CountActiveAsync(scope.Scope, excludeId: null, ct) >= TopicStudyLinkLimits.MaxActiveLinksPerScope)
                return LimitReached();

            var sortOrder = dto.SortOrder
                ?? (await InScope(scope.Scope).MaxAsync(l => (int?)l.SortOrder, ct) ?? -1) + 1;

            var entity = new TopicStudyLink
            {
                TopicId = scope.Scope.TopicId,
                SubTopicId = scope.Scope.SubTopicId,
                Title = fields.Title,
                Url = fields.Url,
                SourceType = fields.SourceType,
                SortOrder = sortOrder,
                IsActive = dto.IsActive,
                CreatedByUserId = user.Id,
                CreatedByName = Truncate(user.FullName, 200),
                CreatedByRole = Truncate(user.Role, 50),
            };

            _context.SetCurrentUser(user.Id);
            _context.TopicStudyLinks.Add(entity);
            await _context.SaveChangesAsync(ct);

            return new TopicStudyLinkResultDto
            {
                Success = true,
                ObjectId = entity.Id,
                Message = _localizer["studyLinks.created"],
                Link = ToDto(entity),
            };
        }, ct);
    }

    public async Task<TopicStudyLinkResultDto> UpdateAsync(int id, UpdateTopicStudyLinkDto dto, int userId, CancellationToken ct = default)
    {
        var fields = ValidateFields(dto.Title, dto.Url, dto.SourceType);
        if (fields.Error != null)
            return Fail<TopicStudyLinkResultDto>(fields.Error);

        return await RunSerializableAsync<TopicStudyLinkResultDto>(async () =>
        {
            var link = await _context.TopicStudyLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (link == null)
                return Fail<TopicStudyLinkResultDto>("studyLinks.notFound", notFound: true);

            var willBeActive = dto.IsActive ?? link.IsActive;
            if (willBeActive && !link.IsActive
                && await CountActiveAsync(ScopeOf(link), excludeId: link.Id, ct) >= TopicStudyLinkLimits.MaxActiveLinksPerScope)
            {
                return LimitReached();
            }

            link.Title = fields.Title;
            link.Url = fields.Url;
            link.SourceType = fields.SourceType;
            link.IsActive = willBeActive;
            if (dto.SortOrder.HasValue)
                link.SortOrder = dto.SortOrder.Value;

            _context.SetCurrentUser(userId);
            await _context.SaveChangesAsync(ct);

            return new TopicStudyLinkResultDto
            {
                Success = true,
                ObjectId = link.Id,
                Message = _localizer["studyLinks.updated"],
                Link = ToDto(link),
            };
        }, ct);
    }

    public async Task<ResponseBaseDto> DeleteAsync(int id, int userId, CancellationToken ct = default)
    {
        var link = await _context.TopicStudyLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link == null)
            return Fail<ResponseBaseDto>("studyLinks.notFound", notFound: true);

        // AppDbContext Remove'u soft delete'e çevirir; silinen link global filtre sayesinde limite sayılmaz.
        _context.SetCurrentUser(userId);
        _context.TopicStudyLinks.Remove(link);
        await _context.SaveChangesAsync(ct);

        return new ResponseBaseDto { Success = true, ObjectId = id, Message = _localizer["studyLinks.deleted"] };
    }

    public async Task<TopicStudyLinkListResultDto> ReorderAsync(ReorderTopicStudyLinksDto dto, int userId, CancellationToken ct = default)
    {
        var items = dto.Items ?? new List<TopicStudyLinkOrderItemDto>();
        if (items.Count == 0)
            return Fail<TopicStudyLinkListResultDto>("studyLinks.reorder.itemsRequired");
        if (items.Count > 100)
            return Fail<TopicStudyLinkListResultDto>("studyLinks.reorder.tooManyItems");
        if (items.Select(i => i.Id).Distinct().Count() != items.Count)
            return Fail<TopicStudyLinkListResultDto>("studyLinks.reorder.duplicateIds");

        var scope = await ResolveExactScopeAsync(dto.TopicId, dto.SubTopicId, ct);
        if (scope.Error != null)
            return Fail<TopicStudyLinkListResultDto>(scope.Error, notFound: scope.NotFound);

        var ids = items.Select(i => i.Id).ToList();
        var links = await InScope(scope.Scope).Where(l => ids.Contains(l.Id)).ToListAsync(ct);
        if (links.Count != ids.Count)
            return Fail<TopicStudyLinkListResultDto>("studyLinks.reorder.linkNotInScope");

        var orderById = items.ToDictionary(i => i.Id, i => i.SortOrder);
        foreach (var link in links)
            link.SortOrder = orderById[link.Id];

        _context.SetCurrentUser(userId);
        await _context.SaveChangesAsync(ct);

        var list = await BuildListAsync(scope.Scope, includeInactive: true, skip: 0, take: 100, ct);
        list.Message = _localizer["studyLinks.reordered"];
        return list;
    }

    // ---------------- Öğrenci sonuç ekranı ----------------

    public async Task<StudyLinkSuggestionsResultDto> GetSuggestionsForResultAsync(int testInstanceId, int studentUserId, CancellationToken ct = default)
    {
        // Sahiplik: sonuç ekranının kendi ucu (TestSessionService.GetCanvasTestResultAsync) ile aynı filtre —
        // instance çağıran öğrenciye ait değilse "yok" say (varlık sızdırılmaz).
        var instance = await _context.TestInstances.AsNoTracking()
            .Where(ti => ti.Id == testInstanceId && ti.Student.UserId == studentUserId)
            .Select(ti => new { ti.Status })
            .FirstOrDefaultAsync(ct);

        if (instance == null)
            return Fail<StudyLinkSuggestionsResultDto>("studyLinks.testInstanceNotFound", notFound: true);

        // Doğru cevaplar (dolayısıyla yanlışlar) yalnızca tamamlanmış sınavda açıklanır — aksi halde
        // bu uç devam eden sınavda hangi cevabın yanlış olduğunu sızdırırdı.
        if (instance.Status != WorksheetInstanceStatus.Completed)
            return new StudyLinkSuggestionsResultDto { Success = true };

        // "Yanlış" = işaretlenmiş VE doğru cevaptan farklı (WorksheetDetailService.CreateWorksheetFromMistakesAsync
        // ve UI sonuç ekranı ile aynı tanım). Boş bırakılan sorular yanlış sayılmaz.
        var wrong = await _context.TestInstanceQuestions.AsNoTracking()
            .Where(wiq => wiq.WorksheetInstanceId == testInstanceId
                && wiq.SelectedAnswerId != null
                && wiq.WorksheetQuestion.Question.CorrectAnswerId != wiq.SelectedAnswerId)
            .Select(wiq => new { wiq.Id, wiq.WorksheetQuestion.QuestionId, wiq.WorksheetQuestion.Order })
            .ToListAsync(ct);

        if (wrong.Count == 0)
            return new StudyLinkSuggestionsResultDto { Success = true };

        var wrongQuestions = wrong
            .OrderBy(w => w.Order).ThenBy(w => w.Id)
            .GroupBy(w => w.QuestionId)
            .Select(g => g.First())
            .ToList();
        var questionIds = wrongQuestions.Select(w => w.QuestionId).ToList();

        var questionSubTopics = await _context.QuestionSubTopics.AsNoTracking()
            .Where(qst => questionIds.Contains(qst.QuestionId))
            .OrderBy(qst => qst.Id)
            .Select(qst => new { qst.QuestionId, qst.SubTopicId, SubTopicName = qst.SubTopic.Name })
            .ToListAsync(ct);

        var subTopicIds = questionSubTopics.Select(x => x.SubTopicId).Distinct().ToList();
        if (subTopicIds.Count == 0)
            return new StudyLinkSuggestionsResultDto { Success = true };

        var links = await _context.TopicStudyLinks.AsNoTracking()
            .Where(l => l.IsActive && l.SubTopicId != null && subTopicIds.Contains(l.SubTopicId.Value))
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
            .Select(l => new
            {
                SubTopicId = l.SubTopicId!.Value,
                Link = new StudyLinkSummaryDto { Id = l.Id, Title = l.Title, Url = l.Url, SourceType = l.SourceType }
            })
            .ToListAsync(ct);

        // Savunma: eşzamanlılık kenar durumunda 7'yi aşan aktif link olsa bile öğrenciye en fazla 7 gösterilir.
        var linksBySubTopic = links
            .GroupBy(x => x.SubTopicId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Link).Take(TopicStudyLinkLimits.MaxActiveLinksPerScope).ToList());

        var subTopicsByQuestion = questionSubTopics
            .GroupBy(x => x.QuestionId)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.SubTopicId).Select(sg => sg.First()).ToList());

        var items = new List<QuestionStudyLinkSuggestionDto>();
        foreach (var q in wrongQuestions)
        {
            if (!subTopicsByQuestion.TryGetValue(q.QuestionId, out var subTopics))
                continue;

            var groups = subTopics
                .Where(st => linksBySubTopic.ContainsKey(st.SubTopicId))
                .Select(st => new SubTopicStudyLinkGroupDto
                {
                    SubTopicId = st.SubTopicId,
                    SubTopicName = st.SubTopicName,
                    Links = linksBySubTopic[st.SubTopicId],
                })
                .ToList();

            if (groups.Count == 0)
                continue;

            items.Add(new QuestionStudyLinkSuggestionDto
            {
                QuestionId = q.QuestionId,
                TestInstanceQuestionId = q.Id,
                Groups = groups,
            });
        }

        return new StudyLinkSuggestionsResultDto { Success = true, Items = items };
    }

    // ---------------- Yardımcılar ----------------

    private readonly record struct LinkScope(int? TopicId, int? SubTopicId);

    private readonly record struct ScopeResolution(LinkScope Scope, string? Error, bool NotFound);

    private readonly record struct FieldValidation(string Title, string Url, TopicStudyLinkSourceType SourceType, string? Error);

    private static LinkScope ScopeOf(TopicStudyLink link) => new(link.TopicId, link.SubTopicId);

    private IQueryable<TopicStudyLink> InScope(LinkScope scope)
    {
        if (scope.SubTopicId is int subTopicId)
            return _context.TopicStudyLinks.Where(l => l.SubTopicId == subTopicId);

        var topicId = scope.TopicId;
        return _context.TopicStudyLinks.Where(l => l.TopicId == topicId && l.SubTopicId == null);
    }

    private Task<int> CountActiveAsync(LinkScope scope, int? excludeId, CancellationToken ct)
    {
        var query = InScope(scope).Where(l => l.IsActive);
        if (excludeId is int id)
            query = query.Where(l => l.Id != id);
        return query.CountAsync(ct);
    }

    /// <summary>Liste/sıralama için: TopicId veya SubTopicId'den TAM OLARAK biri ve kayıt mevcut olmalı.</summary>
    private async Task<ScopeResolution> ResolveExactScopeAsync(int? topicId, int? subTopicId, CancellationToken ct)
    {
        if (topicId.HasValue == subTopicId.HasValue)
            return new ScopeResolution(default, "studyLinks.scopeExactlyOne", false);

        if (subTopicId is int stId)
        {
            var parentTopicId = await _context.SubTopics.AsNoTracking()
                .Where(st => st.Id == stId).Select(st => (int?)st.TopicId).FirstOrDefaultAsync(ct);
            return parentTopicId == null
                ? new ScopeResolution(default, "studyLinks.subTopicNotFound", true)
                : new ScopeResolution(new LinkScope(parentTopicId, stId), null, false);
        }

        var exists = await _context.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId, ct);
        return exists
            ? new ScopeResolution(new LinkScope(topicId, null), null, false)
            : new ScopeResolution(default, "studyLinks.topicNotFound", true);
    }

    /// <summary>
    /// Oluşturma için: en az biri dolu. Alt konu verilmişse konu, alt konunun üst konusundan alınır; ikisi birlikte
    /// verilmişse uyuşmalı. Referans verilen kayıt yoksa 400 (gövde doğrulama hatası).
    /// </summary>
    private async Task<ScopeResolution> ResolveCreateScopeAsync(int? topicId, int? subTopicId, CancellationToken ct)
    {
        if (!topicId.HasValue && !subTopicId.HasValue)
            return new ScopeResolution(default, "studyLinks.scopeRequired", false);

        if (subTopicId is int stId)
        {
            var parentTopicId = await _context.SubTopics.AsNoTracking()
                .Where(st => st.Id == stId).Select(st => (int?)st.TopicId).FirstOrDefaultAsync(ct);
            if (parentTopicId == null)
                return new ScopeResolution(default, "studyLinks.subTopicNotFound", false);
            if (topicId.HasValue && topicId != parentTopicId)
                return new ScopeResolution(default, "studyLinks.subTopicTopicMismatch", false);
            return new ScopeResolution(new LinkScope(parentTopicId, stId), null, false);
        }

        return await _context.Topics.AsNoTracking().AnyAsync(t => t.Id == topicId, ct)
            ? new ScopeResolution(new LinkScope(topicId, null), null, false)
            : new ScopeResolution(default, "studyLinks.topicNotFound", false);
    }

    private static FieldValidation ValidateFields(string? rawTitle, string? rawUrl, TopicStudyLinkSourceType? requestedSource)
    {
        var title = rawTitle?.Trim() ?? string.Empty;
        if (title.Length == 0)
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.titleRequired");
        if (title.Length > TopicStudyLinkLimits.TitleMaxLength)
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.titleTooLong");

        var url = rawUrl?.Trim() ?? string.Empty;
        if (url.Length == 0)
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.urlRequired");
        if (url.Length > TopicStudyLinkLimits.UrlMaxLength)
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.urlTooLong");
        if (!TryParseSafeUrl(url, out var uri))
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.invalidUrl");

        if (requestedSource.HasValue && !Enum.IsDefined(requestedSource.Value))
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.invalidSourceType");

        var isYouTube = IsYouTubeHost(uri.Host);
        if (requestedSource == TopicStudyLinkSourceType.YouTube && !isYouTube)
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.sourceTypeMismatch");

        var source = requestedSource ?? (isYouTube ? TopicStudyLinkSourceType.YouTube : TopicStudyLinkSourceType.Other);
        return new FieldValidation(title, url, source, null);
    }

    /// <summary>
    /// Yalnızca mutlak http/https URL kabul edilir (javascript:, data:, vbscript:, file:, göreli yol vb. reddedilir).
    /// Boşluk/kontrol karakteri içeren ve kullanıcı bilgisi (<c>https://youtube.com@evil.example</c> gibi
    /// oltalama) taşıyan URL'ler de reddedilir. Link öğrenciye tıklanabilir olarak gösterildiği için güvenlik sınırıdır.
    /// </summary>
    private static bool TryParseSafeUrl(string url, out Uri uri)
    {
        uri = null!;
        if (url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        if (string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
            return false;

        uri = parsed;
        return true;
    }

    private static bool IsYouTubeHost(string host)
    {
        host = host.ToLowerInvariant();
        return host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal)
            || host == "youtu.be"
            || host == "youtube-nocookie.com" || host.EndsWith(".youtube-nocookie.com", StringComparison.Ordinal);
    }

    /// <summary>
    /// İşi serializable transaction içinde, execution strategy (retry) ile çalıştırır. Retry'de işin baştan ve temiz
    /// bir change tracker ile koşması için her denemede tracker temizlenir — iş fonksiyonu kendi entity'lerini
    /// transaction İÇİNDE yüklemelidir. Başarısız sonuçta commit edilmez (dispose → rollback).
    /// </summary>
    private async Task<T> RunSerializableAsync<T>(Func<Task<T>> work, CancellationToken ct) where T : ResponseBaseDto, new()
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var result = await work();
                if (result.Success)
                    await tx.CommitAsync(ct);
                return result;
            });
        }
        catch (Exception ex) when (ex is DbUpdateException or RetryLimitExceededException or DbException { IsTransient: true })
        {
            _logger.LogWarning(ex, "Çalışma linki yazılamadı (eşzamanlı değişiklik / serialization failure).");
            return Fail<T>("studyLinks.concurrentModification", conflict: true);
        }
    }

    private async Task<TopicStudyLinkListResultDto> BuildListAsync(LinkScope scope, bool includeInactive, int skip, int take, CancellationToken ct)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var baseQuery = InScope(scope).AsNoTracking();
        var activeCount = await baseQuery.CountAsync(l => l.IsActive, ct);

        var filtered = includeInactive ? baseQuery : baseQuery.Where(l => l.IsActive);
        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
            .Skip(skip).Take(take)
            .Select(ToDtoExpression)
            .ToListAsync(ct);

        return new TopicStudyLinkListResultDto
        {
            Success = true,
            Items = items,
            TotalCount = total,
            ActiveCount = activeCount,
            MaxActiveLinks = TopicStudyLinkLimits.MaxActiveLinksPerScope,
        };
    }

    private TopicStudyLinkResultDto LimitReached() => new()
    {
        Success = false,
        Conflict = true,
        ErrorCode = TopicStudyLinkErrorCodes.ActiveLimitReached,
        Message = _localizer["studyLinks.activeLimitReached", TopicStudyLinkLimits.MaxActiveLinksPerScope],
    };

    private T Fail<T>(string key, bool notFound = false, bool conflict = false) where T : ResponseBaseDto, new() => new()
    {
        Success = false,
        NotFound = notFound,
        Conflict = conflict,
        Message = _localizer[key],
    };

    private static string Truncate(string? value, int max)
    {
        value ??= string.Empty;
        return value.Length <= max ? value : value[..max];
    }

    private static readonly System.Linq.Expressions.Expression<Func<TopicStudyLink, TopicStudyLinkDto>> ToDtoExpression = l => new TopicStudyLinkDto
    {
        Id = l.Id,
        TopicId = l.TopicId,
        SubTopicId = l.SubTopicId,
        Title = l.Title,
        Url = l.Url,
        SourceType = l.SourceType,
        SortOrder = l.SortOrder,
        IsActive = l.IsActive,
        CreatedByUserId = l.CreatedByUserId,
        CreatedByName = l.CreatedByName,
        CreatedByRole = l.CreatedByRole,
        CreateTime = l.CreateTime,
        UpdateTime = l.UpdateTime,
    };

    private static readonly Func<TopicStudyLink, TopicStudyLinkDto> ToDtoCompiled = ToDtoExpression.Compile();

    private static TopicStudyLinkDto ToDto(TopicStudyLink l) => ToDtoCompiled(l);
}
