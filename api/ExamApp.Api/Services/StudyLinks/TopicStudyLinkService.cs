using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.StudyLinks;
using ExamApp.Api.Services.Teachers;
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
/// <para><b>Yetki:</b> Admin her şeyi yapabilir. Teacher yalnızca ONAYLI ise (<see cref="IApprovedTeacherGuard"/>)
/// listeleyebilir/ekleyebilir/sıralayabilir; güncelleme ve silmede yalnızca KENDİ oluşturduğu linke dokunabilir.
/// Sıralama yalnızca SortOrder değiştirir (içerik değil); bu yüzden onaylı her öğretmene açıktır ve denetim kaydına düşer.
/// Moderasyon yoktur — link eklendiği an yayındadır; her değişiklik <see cref="TopicStudyLinkAudit"/>'e yazılır.</para>
///
/// <para><b>Kapsam anahtarı:</b> SubTopicId doluysa alt konu, değilse konu (TopicId dolu, SubTopicId boş).
/// Alt konu linkinde TopicId alt konunun üst konusuyla doldurulur ama sayım/listeleme alt konu üzerinden yapılır.</para>
///
/// <para><b>Limitler ve eşzamanlılık:</b> kapsam başına en fazla 7 aktif ve 30 toplam (aktif + pasif) link.
/// "say → ekle/aktifleştir" iki ayrı SQL ifadesi olduğu için iki yönetici aynı anda son hakkı kullanırsa ikisi de
/// limitin altında görüp limiti aşabilirdi (write skew). Bu yüzden sayım ve yazma <see cref="IsolationLevel.Serializable"/>
/// transaction içinde yapılır: PostgreSQL SSI çakışan ikinci transaction'ı serialization_failure (40001) ile düşürür;
/// Npgsql execution strategy'si (Aspire AddNpgsqlDbContext → retry açık) işlemi baştan dener, ikinci denemede sayım
/// limiti görür ve limit hatası döner. Retry tükenirse 409 (concurrentModification). Öğrenci tarafı savunma amaçlı
/// grup başına en fazla 7 link gösterir.</para>
///
/// <para><b>URL:</b> yalnızca mutlak http/https, DNS host (IP literal ve localhost yok), kullanıcı bilgisi yok.
/// Saklanan değer normalize edilmiş <c>AbsoluteUri</c>'dir (IDN host punycode'a çevrilir).</para>
/// </summary>
public class TopicStudyLinkService : ITopicStudyLinkService
{
    private readonly AppDbContext _context;
    private readonly IApprovedTeacherGuard _teacherGuard;
    private readonly ILogger<TopicStudyLinkService> _logger;

    // Client'a ulaşan tüm metinler mesaj sözlüğünden gelir (issue #184). Opsiyonel parametreler yalnızca DI'sız
    // (birim test) kurulum için; DI her zaman gerçek bağımlılıkları verir.
    private readonly IStringLocalizer<Messages> _localizer;

    public TopicStudyLinkService(
        AppDbContext context,
        IApprovedTeacherGuard? teacherGuard = null,
        ILogger<TopicStudyLinkService>? logger = null,
        IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _teacherGuard = teacherGuard ?? new ApprovedTeacherGuard(context);
        _logger = logger ?? NullLogger<TopicStudyLinkService>.Instance;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    // ---------------- Yönetim (Admin / onaylı Teacher) ----------------

    public async Task<TopicStudyLinkListResultDto> ListAsync(TopicStudyLinkQueryDto query, StudyLinkActor actor, CancellationToken ct = default)
    {
        if (await AuthorizeManagerAsync<TopicStudyLinkListResultDto>(actor, ct) is { } denied)
            return denied;

        var scope = await ResolveExactScopeAsync(query.TopicId, query.SubTopicId, ct);
        if (scope.Error != null)
            return Fail<TopicStudyLinkListResultDto>(scope.Error, notFound: scope.NotFound);

        return await BuildListAsync(scope.Scope, query.IncludeInactive, query.Skip, query.Take, ct);
    }

    public async Task<TopicStudyLinkResultDto> GetByIdAsync(int id, StudyLinkActor actor, CancellationToken ct = default)
    {
        if (await AuthorizeManagerAsync<TopicStudyLinkResultDto>(actor, ct) is { } denied)
            return denied;

        var link = await _context.TopicStudyLinks.AsNoTracking()
            .Where(l => l.Id == id)
            .Select(ToDtoExpression)
            .FirstOrDefaultAsync(ct);

        return link == null
            ? Fail<TopicStudyLinkResultDto>("studyLinks.notFound", notFound: true)
            : new TopicStudyLinkResultDto { Success = true, ObjectId = link.Id, Link = link };
    }

    public async Task<TopicStudyLinkResultDto> CreateAsync(CreateTopicStudyLinkDto dto, StudyLinkActor actor, CancellationToken ct = default)
    {
        if (await AuthorizeManagerAsync<TopicStudyLinkResultDto>(actor, ct) is { } denied)
            return denied;

        var fields = ValidateFields(dto.Title, dto.Url, dto.SourceType);
        if (fields.Error != null)
            return Fail<TopicStudyLinkResultDto>(fields.Error);

        var scope = await ResolveCreateScopeAsync(dto.TopicId, dto.SubTopicId, ct);
        if (scope.Error != null)
            return Fail<TopicStudyLinkResultDto>(scope.Error);

        return await RunSerializableAsync<TopicStudyLinkResultDto>(async () =>
        {
            if (await InScope(scope.Scope).CountAsync(ct) >= TopicStudyLinkLimits.MaxTotalLinksPerScope)
                return LimitReached(TopicStudyLinkErrorCodes.TotalLimitReached, "studyLinks.totalLimitReached", TopicStudyLinkLimits.MaxTotalLinksPerScope);

            if (dto.IsActive && await CountActiveAsync(scope.Scope, excludeId: null, ct) >= TopicStudyLinkLimits.MaxActiveLinksPerScope)
                return LimitReached(TopicStudyLinkErrorCodes.ActiveLimitReached, "studyLinks.activeLimitReached", TopicStudyLinkLimits.MaxActiveLinksPerScope);

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
                CreatedByUserId = actor.UserId,
                CreatedByName = Truncate(actor.Name, 200),
                CreatedByRole = Truncate(actor.Role, 50),
            };

            _context.SetCurrentUser(actor.UserId);
            _context.TopicStudyLinks.Add(entity);
            // Link + denetim kaydı aynı SaveChanges (ve serializable transaction) içinde.
            AddAudit(entity, TopicStudyLinkAuditAction.Create, actor, oldUrl: null, oldTitle: null);
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

    public async Task<TopicStudyLinkResultDto> UpdateAsync(int id, UpdateTopicStudyLinkDto dto, StudyLinkActor actor, CancellationToken ct = default)
    {
        if (await AuthorizeManagerAsync<TopicStudyLinkResultDto>(actor, ct) is { } denied)
            return denied;

        var fields = ValidateFields(dto.Title, dto.Url, dto.SourceType);
        if (fields.Error != null)
            return Fail<TopicStudyLinkResultDto>(fields.Error);

        return await RunSerializableAsync<TopicStudyLinkResultDto>(async () =>
        {
            var link = await _context.TopicStudyLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
            if (link == null)
                return Fail<TopicStudyLinkResultDto>("studyLinks.notFound", notFound: true);

            if (DenyIfNotOwner(link, actor) is { } notOwner)
                return notOwner;

            var wasActive = link.IsActive;
            var willBeActive = dto.IsActive ?? link.IsActive;
            if (willBeActive && !wasActive
                && await CountActiveAsync(ScopeOf(link), excludeId: link.Id, ct) >= TopicStudyLinkLimits.MaxActiveLinksPerScope)
            {
                return LimitReached(TopicStudyLinkErrorCodes.ActiveLimitReached, "studyLinks.activeLimitReached", TopicStudyLinkLimits.MaxActiveLinksPerScope);
            }

            var oldUrl = link.Url;
            var oldTitle = link.Title;

            link.Title = fields.Title;
            link.Url = fields.Url;
            link.SourceType = fields.SourceType;
            link.IsActive = willBeActive;
            if (dto.SortOrder.HasValue)
                link.SortOrder = dto.SortOrder.Value;
            link.UpdatedByName = Truncate(actor.Name, 200);

            var action = (wasActive, willBeActive) switch
            {
                (false, true) => TopicStudyLinkAuditAction.Activate,
                (true, false) => TopicStudyLinkAuditAction.Deactivate,
                _ => TopicStudyLinkAuditAction.Update,
            };

            _context.SetCurrentUser(actor.UserId);
            AddAudit(link, action, actor, oldUrl, oldTitle);
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

    public async Task<TopicStudyLinkResultDto> DeleteAsync(int id, StudyLinkActor actor, CancellationToken ct = default)
    {
        if (await AuthorizeManagerAsync<TopicStudyLinkResultDto>(actor, ct) is { } denied)
            return denied;

        var link = await _context.TopicStudyLinks.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (link == null)
            return Fail<TopicStudyLinkResultDto>("studyLinks.notFound", notFound: true);

        if (DenyIfNotOwner(link, actor) is { } notOwner)
            return notOwner;

        // AppDbContext Remove'u soft delete'e çevirir; silinen link global filtre sayesinde limitlere sayılmaz.
        // Soft delete + denetim kaydı tek SaveChanges → tek transaction.
        _context.SetCurrentUser(actor.UserId);
        link.UpdatedByName = Truncate(actor.Name, 200);
        _context.TopicStudyLinks.Remove(link);
        AddAudit(link, TopicStudyLinkAuditAction.Delete, actor, link.Url, link.Title, includeNew: false);
        await _context.SaveChangesAsync(ct);

        return new TopicStudyLinkResultDto { Success = true, ObjectId = id, Message = _localizer["studyLinks.deleted"] };
    }

    public async Task<TopicStudyLinkListResultDto> ReorderAsync(ReorderTopicStudyLinksDto dto, StudyLinkActor actor, CancellationToken ct = default)
    {
        if (await AuthorizeManagerAsync<TopicStudyLinkListResultDto>(actor, ct) is { } denied)
            return denied;

        var items = dto.Items ?? new List<TopicStudyLinkOrderItemDto>();
        if (items.Count == 0)
            return Fail<TopicStudyLinkListResultDto>("studyLinks.reorder.itemsRequired");
        if (items.Count > 100)
            return Fail<TopicStudyLinkListResultDto>("studyLinks.reorder.tooManyItems");
        if (items.Select(i => i.Id).Distinct().Count() != items.Count)
            return Fail<TopicStudyLinkListResultDto>("studyLinks.reorder.duplicateIds");
        if (items.Select(i => i.SortOrder).Distinct().Count() != items.Count)
            return Fail<TopicStudyLinkListResultDto>("studyLinks.reorder.duplicateSortOrders");

        var scope = await ResolveExactScopeAsync(dto.TopicId, dto.SubTopicId, ct);
        if (scope.Error != null)
            return Fail<TopicStudyLinkListResultDto>(scope.Error, notFound: scope.NotFound);

        var ids = items.Select(i => i.Id).ToList();
        var links = await InScope(scope.Scope).Where(l => ids.Contains(l.Id)).ToListAsync(ct);
        if (links.Count != ids.Count)
            return Fail<TopicStudyLinkListResultDto>("studyLinks.reorder.linkNotInScope");

        var orderById = items.ToDictionary(i => i.Id, i => i.SortOrder);
        foreach (var link in links.Where(l => l.SortOrder != orderById[l.Id]))
        {
            link.SortOrder = orderById[link.Id];
            link.UpdatedByName = Truncate(actor.Name, 200);
            AddAudit(link, TopicStudyLinkAuditAction.Reorder, actor, oldUrl: null, oldTitle: null, includeNew: false);
        }

        _context.SetCurrentUser(actor.UserId);
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
            .Select(qst => new { qst.QuestionId, qst.SubTopicId, SubTopicName = qst.SubTopic.Name, qst.SubTopic.TopicId })
            .ToListAsync(ct);

        // Sorunun konu(ları): önce Question.TopicId (opsiyonel, doğrudan bağ), ardından alt konularının üst konuları.
        var questionTopicIds = await _context.Questions.AsNoTracking()
            .Where(q => questionIds.Contains(q.Id) && q.TopicId != null)
            .Select(q => new { q.Id, TopicId = q.TopicId!.Value })
            .ToDictionaryAsync(x => x.Id, x => x.TopicId, ct);

        var subTopicsByQuestion = questionSubTopics
            .GroupBy(x => x.QuestionId)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => x.SubTopicId).Select(sg => sg.First()).ToList());

        var topicsByQuestion = questionIds.ToDictionary(qid => qid, qid =>
        {
            var ids = new List<int>();
            if (questionTopicIds.TryGetValue(qid, out var direct))
                ids.Add(direct);
            if (subTopicsByQuestion.TryGetValue(qid, out var sts))
                ids.AddRange(sts.Select(st => st.TopicId));
            return ids.Distinct().ToList();
        });

        var subTopicIds = questionSubTopics.Select(x => x.SubTopicId).Distinct().ToList();
        var topicIds = topicsByQuestion.Values.SelectMany(v => v).Distinct().ToList();
        if (subTopicIds.Count == 0 && topicIds.Count == 0)
            return new StudyLinkSuggestionsResultDto { Success = true };

        // Tek sorgu: alt konu linkleri + konu seviyesi (SubTopicId boş) linkler.
        var links = await _context.TopicStudyLinks.AsNoTracking()
            .Where(l => l.IsActive
                && ((l.SubTopicId != null && subTopicIds.Contains(l.SubTopicId.Value))
                    || (l.SubTopicId == null && l.TopicId != null && topicIds.Contains(l.TopicId.Value))))
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Id)
            .Select(l => new
            {
                l.SubTopicId,
                l.TopicId,
                Link = new StudyLinkSummaryDto { Id = l.Id, Title = l.Title, Url = l.Url, SourceType = l.SourceType }
            })
            .ToListAsync(ct);

        // Savunma: eşzamanlılık kenar durumunda 7'yi aşan aktif link olsa bile öğrenciye en fazla 7 gösterilir.
        var linksBySubTopic = links
            .Where(x => x.SubTopicId != null)
            .GroupBy(x => x.SubTopicId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Link).Take(TopicStudyLinkLimits.MaxActiveLinksPerScope).ToList());
        var linksByTopic = links
            .Where(x => x.SubTopicId == null)
            .GroupBy(x => x.TopicId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Link).Take(TopicStudyLinkLimits.MaxActiveLinksPerScope).ToList());

        var topicNames = linksByTopic.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Topics.AsNoTracking()
                .Where(t => linksByTopic.Keys.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);

        var items = new List<QuestionStudyLinkSuggestionDto>();
        foreach (var q in wrongQuestions)
        {
            var groups = new List<StudyLinkGroupDto>();

            if (subTopicsByQuestion.TryGetValue(q.QuestionId, out var subTopics))
            {
                groups.AddRange(subTopics
                    .Where(st => linksBySubTopic.ContainsKey(st.SubTopicId))
                    .Select(st => new StudyLinkGroupDto
                    {
                        Kind = StudyLinkGroupKind.SubTopic,
                        SubTopicId = st.SubTopicId,
                        TopicId = st.TopicId,
                        Name = st.SubTopicName,
                        Links = linksBySubTopic[st.SubTopicId],
                    }));
            }

            // Alt konu gruplarından sonra, sorunun her farklı konusu için tek bir konu seviyesi yedek grup.
            groups.AddRange(topicsByQuestion[q.QuestionId]
                .Where(tid => linksByTopic.ContainsKey(tid) && topicNames.ContainsKey(tid))
                .Select(tid => new StudyLinkGroupDto
                {
                    Kind = StudyLinkGroupKind.Topic,
                    TopicId = tid,
                    Name = topicNames[tid],
                    Links = linksByTopic[tid],
                }));

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

    // ---------------- Yetki / denetim ----------------

    /// <summary>Admin → null (izin). Teacher → onaylı değilse Forbidden + TeacherNotApproved.</summary>
    private async Task<T?> AuthorizeManagerAsync<T>(StudyLinkActor actor, CancellationToken ct) where T : StudyLinkResponseDto, new()
    {
        if (actor.IsAdmin)
            return null;

        if (await _teacherGuard.CheckAsync(actor.UserId, ct) == TeacherApprovalCheck.Approved)
            return null;

        _logger.LogWarning("Onaylı olmayan öğretmen çalışma linki yönetimi denedi. UserId={UserId}", actor.UserId);
        var result = Fail<T>("studyLinks.teacherNotApproved");
        result.Forbidden = true;
        result.ErrorCode = TopicStudyLinkErrorCodes.TeacherNotApproved;
        return result;
    }

    /// <summary>Teacher yalnızca kendi linkini güncelleyebilir/silebilir; Admin her linki.</summary>
    private TopicStudyLinkResultDto? DenyIfNotOwner(TopicStudyLink link, StudyLinkActor actor)
    {
        if (actor.IsAdmin || link.CreatedByUserId == actor.UserId)
            return null;

        var result = Fail<TopicStudyLinkResultDto>("studyLinks.notOwner");
        result.Forbidden = true;
        result.ErrorCode = TopicStudyLinkErrorCodes.NotOwner;
        return result;
    }

    private void AddAudit(TopicStudyLink link, TopicStudyLinkAuditAction action, StudyLinkActor actor,
        string? oldUrl, string? oldTitle, bool includeNew = true)
    {
        _context.TopicStudyLinkAudits.Add(new TopicStudyLinkAudit
        {
            Link = link,
            Action = action,
            ActorUserId = actor.UserId,
            ActorRole = Truncate(actor.Role, 50),
            OccurredAtUtc = DateTime.UtcNow,
            OldUrl = oldUrl,
            OldTitle = oldTitle,
            NewUrl = includeNew ? link.Url : null,
            NewTitle = includeNew ? link.Title : null,
        });
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
        if (!TryNormalizeSafeUrl(url, out var normalizedUrl, out var host))
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.invalidUrl");
        // Normalizasyon (punycode, yüzde-kodlama) uzatabilir — sınır saklanan değer için de geçerli.
        if (normalizedUrl.Length > TopicStudyLinkLimits.UrlMaxLength)
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.urlTooLong");

        if (requestedSource.HasValue && !Enum.IsDefined(requestedSource.Value))
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.invalidSourceType");

        var isYouTube = IsYouTubeHost(host);
        if (requestedSource == TopicStudyLinkSourceType.YouTube && !isYouTube)
            return new FieldValidation(string.Empty, string.Empty, default, "studyLinks.sourceTypeMismatch");

        var source = requestedSource ?? (isYouTube ? TopicStudyLinkSourceType.YouTube : TopicStudyLinkSourceType.Other);
        return new FieldValidation(title, normalizedUrl, source, null);
    }

    /// <summary>
    /// Güvenlik sınırı — link öğrenciye tıklanabilir olarak gösterilir. Kabul koşulları:
    /// <list type="bullet">
    ///   <item>mutlak <c>http</c>/<c>https</c> (javascript:, data:, vbscript:, file:, ftp:, göreli yol reddedilir);</item>
    ///   <item>boşluk/kontrol karakteri yok; kullanıcı bilgisi yok (<c>https://youtube.com@evil.example</c> oltalaması);</item>
    ///   <item>host bir DNS adı: IPv4 (ondalık/hex/oktal/tamsayı yazımları dahil — Uri bunları IPv4'e çözer) ve IPv6
    ///   literal'leri ile <c>localhost</c> / <c>*.localhost</c> reddedilir;</item>
    ///   <item>IDN host punycode'a çevrilir (<see cref="Uri.IdnHost"/>) ve normalize sonrası ASCII olmayan host reddedilir.</item>
    /// </list>
    /// <paramref name="normalized"/> saklanacak değerdir: punycode host ile yeniden kurulmuş <see cref="Uri.AbsoluteUri"/>.
    /// </summary>
    private static bool TryNormalizeSafeUrl(string url, out string normalized, out string host)
    {
        normalized = string.Empty;
        host = string.Empty;

        if (url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        if (string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo))
            return false;
        if (parsed.HostNameType != UriHostNameType.Dns)
            return false; // IPv4 / IPv6 / Basic / Unknown

        var idnHost = parsed.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (idnHost.Length == 0 || idnHost.Any(c => c > 0x7F))
            return false;
        if (idnHost == "localhost" || idnHost.EndsWith(".localhost", StringComparison.Ordinal))
            return false;
        // Savunma: Uri'nin DNS saydığı ama IPAddress'in (inet_aton tarzı: hex/oktal/tamsayı) IP olarak çözdüğü yazımlar.
        if (IPAddress.TryParse(idnHost, out _))
            return false;

        var builder = new UriBuilder(parsed) { Host = idnHost };
        if (parsed.IsDefaultPort)
            builder.Port = -1;

        normalized = builder.Uri.AbsoluteUri;
        host = idnHost;
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
        catch (Exception ex) when (IsSerializationConflict(ex))
        {
            // Yalnızca eşzamanlılık çakışması 409'a çevrilir; diğer DB hataları (kısıt ihlali, bağlantı vb.) yukarı
            // fırlar ve 500 olarak loglanır — gerçek hataları "tekrar deneyin" mesajının arkasına saklamayalım.
            _logger.LogWarning(ex, "Çalışma linki yazılamadı (eşzamanlı değişiklik / serialization failure).");
            return Fail<T>("studyLinks.concurrentModification", conflict: true);
        }
    }

    /// <summary>
    /// Serializable transaction çakışması mı? PostgreSQL <c>40001</c> (serialization_failure) / <c>40P01</c>
    /// (deadlock_detected) — doğrudan (commit/sorgu), <see cref="DbUpdateException"/> içinde (SaveChanges) ya da
    /// execution strategy'nin retry'leri tükettiğinde fırlattığı <see cref="RetryLimitExceededException"/>.
    /// </summary>
    internal static bool IsSerializationConflict(Exception? ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            switch (current)
            {
                case RetryLimitExceededException:
                    return true;
                case Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.SerializationFailure or Npgsql.PostgresErrorCodes.DeadlockDetected }:
                    return true;
            }
        }

        return false;
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

    private TopicStudyLinkResultDto LimitReached(string errorCode, string messageKey, int limit) => new()
    {
        Success = false,
        Conflict = true,
        ErrorCode = errorCode,
        Message = _localizer[messageKey, limit],
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
        UpdatedByUserId = l.UpdateUserId,
        UpdatedByName = l.UpdatedByName,
        UpdateTime = l.UpdateTime,
    };

    private static readonly Func<TopicStudyLink, TopicStudyLinkDto> ToDtoCompiled = ToDtoExpression.Compile();

    private static TopicStudyLinkDto ToDto(TopicStudyLink l) => ToDtoCompiled(l);
}
