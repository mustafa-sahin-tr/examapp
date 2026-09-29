using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Classifier;
using ExamApp.Foundation.Localization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Services.Taxonomy;

public class TaxonomyService : ITaxonomyService
{
    // Debounce window: a burst of edits schedules several jobs, but only the
    // first finds the cache stale and rebuilds — the rest no-op.
    private static readonly TimeSpan ReconcileDelay = TimeSpan.FromMinutes(2);

    private readonly AppDbContext _context;
    private readonly IBackgroundJobClient _jobs;

    // Client'a ulaşan ResponseBaseDto.Message metinleri buradan gelir (issue #184). DI her zaman
    // gerçek localizer'ı verir; parametre yalnızca DI'sız (birim test) senaryolar için opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public TaxonomyService(AppDbContext context, IBackgroundJobClient jobs, IStringLocalizer<Messages>? localizer = null)
    {
        _context = context;
        _jobs = jobs;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    public async Task<TaxonomyTreeDto> GetTreeAsync(int? gradeId = null, bool unassignedOnly = false, CancellationToken ct = default)
    {
        var grades = await _context.Grades
            .OrderBy(g => g.Id)
            .Select(g => new TaxonomyGradeDto { Id = g.Id, Name = g.Name })
            .ToListAsync(ct);

        // Subject -> linked grade ids (GradeSubject). Soft-deleted links are excluded by the global filter.
        var gradeIdsBySubject = (await _context.GradeSubjects
                .AsNoTracking()
                .Select(gs => new { gs.SubjectId, gs.GradeId })
                .Distinct()
                .ToListAsync(ct))
            .GroupBy(x => x.SubjectId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.GradeId).OrderBy(x => x).ToList());

        IQueryable<Subject> subjectQuery = _context.Subjects.AsNoTracking();
        if (unassignedOnly)
            subjectQuery = subjectQuery.Where(s => !_context.GradeSubjects.Any(gs => gs.SubjectId == s.Id));
        else if (gradeId.HasValue)
            subjectQuery = subjectQuery.Where(s => _context.GradeSubjects.Any(gs => gs.SubjectId == s.Id && gs.GradeId == gradeId.Value));

        var subjects = await subjectQuery.OrderBy(s => s.Name).ToListAsync(ct);
        var subjectIds = subjects.Select(s => s.Id).ToList();

        var topics = await _context.Topics.AsNoTracking()
            .Where(t => subjectIds.Contains(t.SubjectId))
            .OrderBy(t => t.Name).ToListAsync(ct);
        var topicIds = topics.Select(t => t.Id).ToList();
        var subTopics = await _context.SubTopics.AsNoTracking()
            .Where(st => topicIds.Contains(st.TopicId))
            .OrderBy(st => st.Name).ToListAsync(ct);

        // Question counts per subtopic (through the join table), single query.
        var counts = await _context.QuestionSubTopics
            .GroupBy(qst => qst.SubTopicId)
            .Select(g => new { SubTopicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SubTopicId, x => x.Count, ct);

        var gradeNames = grades.ToDictionary(g => g.Id, g => g.Name);

        var subTopicsByTopic = subTopics
            .GroupBy(st => st.TopicId)
            .ToDictionary(g => g.Key, g => g.Select(st => new TaxonomySubTopicDto
            {
                Id = st.Id,
                Name = st.Name,
                TopicId = st.TopicId,
                QuestionCount = counts.TryGetValue(st.Id, out var c) ? c : 0,
            }).ToList());

        var topicsBySubject = topics
            .GroupBy(t => t.SubjectId)
            .ToDictionary(g => g.Key, g => g.Select(t => new TaxonomyTopicDto
            {
                Id = t.Id,
                Name = t.Name,
                SubjectId = t.SubjectId,
                GradeId = t.GradeId,
                GradeName = gradeNames.TryGetValue(t.GradeId, out var gn) ? gn : null,
                SubTopics = subTopicsByTopic.TryGetValue(t.Id, out var sts) ? sts : new(),
            }).ToList());

        return new TaxonomyTreeDto
        {
            Grades = grades,
            Subjects = subjects.Select(s => new TaxonomySubjectDto
            {
                Id = s.Id,
                Name = s.Name,
                GradeIds = gradeIdsBySubject.TryGetValue(s.Id, out var gids) ? gids : new(),
                Topics = topicsBySubject.TryGetValue(s.Id, out var ts) ? ts : new(),
            }).ToList(),
        };
    }

    // ---- Subject ----

    public async Task<TaxonomyResponseDto> CreateSubjectAsync(UpsertSubjectDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail(_localizer["taxonomy.subject.nameRequired"]);

        if (await _context.Subjects.AnyAsync(s => s.Name.ToLower() == name.ToLower(), ct))
            return Fail(_localizer["taxonomy.subject.nameAlreadyExists"]);

        // Issue #249: a subject with no grade link never shows up on the grade-filtered admin screen.
        var gradeIds = NormalizeGradeIds(dto.GradeIds);
        if (gradeIds == null)
            return Fail(_localizer["taxonomy.subject.gradeRequired"], TaxonomyErrorCodes.GradeRequired);
        if (!await AllGradesExistAsync(gradeIds, ct))
            return Fail(_localizer["taxonomy.grade.invalid"]);

        _context.SetCurrentUser(userId);
        // A brand-new subject has no existing links, so no sync is needed: attach the
        // GradeSubject rows through the navigation and persist subject + links in ONE
        // SaveChanges (single transaction). EF fills SubjectId once the id is generated.
        var subject = new Subject
        {
            Name = name,
            GradeSubjects = gradeIds
                .Select(gradeId => new GradeSubject { GradeId = gradeId })
                .ToList(),
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync(ct);

        return Ok(_localizer["taxonomy.subject.created"], subject.Id, userId);
    }

    public async Task<TaxonomyResponseDto> UpdateSubjectAsync(int id, UpsertSubjectDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail(_localizer["taxonomy.subject.nameRequired"]);

        var subject = await _context.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subject == null)
            return Fail(_localizer["taxonomy.subject.notFound"]);

        if (await _context.Subjects.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), ct))
            return Fail(_localizer["taxonomy.subject.otherNameAlreadyExists"]);

        var gradeIds = NormalizeGradeIds(dto.GradeIds);
        if (gradeIds != null && !await AllGradesExistAsync(gradeIds, ct))
            return Fail(_localizer["taxonomy.grade.invalid"]);

        // Issue #249: every guard runs before anything is mutated — one blocked link rejects the
        // whole request (name included), so a partial sync is never written. The "at least one grade"
        // rule needs no check here: an empty/null GradeIds means "leave links alone" (NormalizeGradeIds),
        // so a sync can never target an empty set. Same accepted TOCTOU as RemoveSubjectGradeAsync.
        List<GradeSubject>? existingLinks = null;
        if (gradeIds != null)
        {
            existingLinks = await _context.GradeSubjects
                .Where(gs => gs.SubjectId == subject.Id)
                .ToListAsync(ct);
            var removedGradeIds = existingLinks
                .Select(gs => gs.GradeId)
                .Where(g => !gradeIds.Contains(g))
                .Distinct()
                .ToList();
            var blocked = await GradeNamesWithTopicsAsync(subject.Id, removedGradeIds, ct);
            if (blocked.Count > 0)
                return Fail(_localizer["taxonomy.gradeSubject.hasTopics", string.Join(", ", blocked)],
                    TaxonomyErrorCodes.SubjectGradeHasTopics);
        }

        _context.SetCurrentUser(userId);
        subject.Name = name;
        if (gradeIds != null)
            SyncSubjectGrades(subject.Id, existingLinks!, gradeIds);
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.subject.updated"], subject.Id, userId);
    }

    // ---- Subject <-> Grade (GradeSubject) ----

    public async Task<TaxonomyResponseDto> AddSubjectGradeAsync(int subjectId, int gradeId, int userId, CancellationToken ct = default)
    {
        if (!await _context.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            return Fail(_localizer["taxonomy.subject.notFound"]);
        if (!await _context.Grades.AnyAsync(g => g.Id == gradeId, ct))
            return Fail(_localizer["taxonomy.grade.notFound"]);

        if (await _context.GradeSubjects.AnyAsync(gs => gs.SubjectId == subjectId && gs.GradeId == gradeId, ct))
            return Ok(_localizer["taxonomy.gradeSubject.alreadyLinked"], subjectId, userId);

        _context.SetCurrentUser(userId);
        _context.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.gradeSubject.linked"], subjectId, userId);
    }

    public async Task<TaxonomyResponseDto> RemoveSubjectGradeAsync(int subjectId, int gradeId, int userId, CancellationToken ct = default)
    {
        if (!await _context.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            return Fail(_localizer["taxonomy.subject.notFound"]);
        if (!await _context.Grades.AnyAsync(g => g.Id == gradeId, ct))
            return Fail(_localizer["taxonomy.grade.notFound"]);

        var links = await _context.GradeSubjects
            .Where(gs => gs.SubjectId == subjectId && gs.GradeId == gradeId)
            .ToListAsync(ct);
        if (links.Count == 0)
            return Ok(_localizer["taxonomy.gradeSubject.alreadyUnlinked"], subjectId, userId);

        // Issue #249 guards. Check-then-write without a transaction (TOCTOU): a topic/link created
        // concurrently between the checks and SaveChanges can slip through. Accepted on purpose —
        // admin-only endpoints, very low concurrency, and the result is reversible from the same
        // screen ("Sınıfları yönet" re-link). Do not add a transaction/lock just for this.
        //
        // 1) A subject must keep at least one grade link (otherwise it is unreachable from the
        //    grade-filtered admin screen). Deleting the subject is the way to drop it entirely.
        if (!await _context.GradeSubjects.AnyAsync(gs => gs.SubjectId == subjectId && gs.GradeId != gradeId, ct))
            return Fail(_localizer["taxonomy.gradeSubject.lastLink"], TaxonomyErrorCodes.LastGradeLink);

        // 2) This subject's topics in this grade would become unreachable ("sahipsiz").
        var blocked = await GradeNamesWithTopicsAsync(subjectId, new List<int> { gradeId }, ct);
        if (blocked.Count > 0)
            return Fail(_localizer["taxonomy.gradeSubject.hasTopics", string.Join(", ", blocked)],
                TaxonomyErrorCodes.SubjectGradeHasTopics);

        // Only the link row goes (soft delete via ApplyAuditInfo).
        _context.SetCurrentUser(userId);
        _context.GradeSubjects.RemoveRange(links);
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.gradeSubject.unlinked"], subjectId, userId);
    }

    /// <summary>Null when the caller did not send grade ids (or sent an empty list) — meaning "leave links alone".</summary>
    private static List<int>? NormalizeGradeIds(List<int>? gradeIds)
    {
        if (gradeIds == null || gradeIds.Count == 0) return null;
        return gradeIds.Distinct().ToList();
    }

    private async Task<bool> AllGradesExistAsync(List<int> gradeIds, CancellationToken ct)
    {
        var found = await _context.Grades.CountAsync(g => gradeIds.Contains(g.Id), ct);
        return found == gradeIds.Count;
    }

    /// <summary>
    /// Names (ordered by grade id) of the grades among <paramref name="gradeIds"/> in which the subject
    /// still has active topics — links that must not be removed (issue #249).
    /// </summary>
    private async Task<List<string>> GradeNamesWithTopicsAsync(int subjectId, List<int> gradeIds, CancellationToken ct)
    {
        if (gradeIds.Count == 0) return new List<string>();

        // Blocking grade ids come from Topics first, so a soft-deleted Grade (hidden by the global
        // query filter) cannot make a blocked link look free.
        var blockingIds = await _context.Topics
            .Where(t => t.SubjectId == subjectId && gradeIds.Contains(t.GradeId))
            .Select(t => t.GradeId)
            .Distinct()
            .ToListAsync(ct);
        if (blockingIds.Count == 0) return new List<string>();

        // Names are only for the message — include soft-deleted grades, fall back to the id.
        var names = await _context.Grades.IgnoreQueryFilters()
            .Where(g => blockingIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Name, ct);
        return blockingIds
            .OrderBy(id => id)
            .Select(id => names.TryGetValue(id, out var n) ? n : id.ToString())
            .ToList();
    }

    /// <summary>
    /// Makes the subject's GradeSubject links (<paramref name="existing"/>, already loaded and tracked)
    /// equal to <paramref name="desiredGradeIds"/>: missing links are added, links not in the list are
    /// removed. Caller has already checked the removals against topics, and saves.
    /// </summary>
    private void SyncSubjectGrades(int subjectId, List<GradeSubject> existing, List<int> desiredGradeIds)
    {
        var existingGradeIds = existing.Select(gs => gs.GradeId).ToHashSet();
        var desired = desiredGradeIds.ToHashSet();

        var toRemove = existing.Where(gs => !desired.Contains(gs.GradeId)).ToList();
        if (toRemove.Count > 0)
            _context.GradeSubjects.RemoveRange(toRemove);

        foreach (var gradeId in desired.Where(g => !existingGradeIds.Contains(g)))
            _context.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
    }

    public async Task<TaxonomyResponseDto> DeleteSubjectAsync(int id, int userId, CancellationToken ct = default)
    {
        var subject = await _context.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subject == null)
            return Fail(_localizer["taxonomy.subject.notFound"]);

        if (await _context.Topics.AnyAsync(t => t.SubjectId == id, ct))
            return Fail(_localizer["taxonomy.subject.hasTopics"]);

        if (await _context.Questions.AnyAsync(q => q.SubjectId == id, ct))
            return Fail(_localizer["taxonomy.subject.hasQuestions"]);

        _context.SetCurrentUser(userId);
        _context.Subjects.Remove(subject); // soft delete via AppDbContext.ApplyAuditInfo
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.subject.deleted"], id, userId);
    }

    // ---- Topic ----

    public async Task<TaxonomyResponseDto> CreateTopicAsync(UpsertTopicDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail(_localizer["taxonomy.topic.nameRequired"]);
        if (!await _context.Subjects.AnyAsync(s => s.Id == dto.SubjectId, ct))
            return Fail(_localizer["taxonomy.subject.invalid"]);
        if (!await _context.Grades.AnyAsync(g => g.Id == dto.GradeId, ct))
            return Fail(_localizer["taxonomy.grade.invalid"]);
        if (!await IsSubjectLinkedToGradeAsync(dto.SubjectId, dto.GradeId, ct))
            return Fail(_localizer["taxonomy.topic.subjectNotLinkedToGrade"], TaxonomyErrorCodes.SubjectGradeNotLinked);

        _context.SetCurrentUser(userId);
        var topic = new Topic { Name = name, SubjectId = dto.SubjectId, GradeId = dto.GradeId };
        _context.Topics.Add(topic);
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.topic.created"], topic.Id, userId);
    }

    public async Task<TaxonomyResponseDto> UpdateTopicAsync(int id, UpsertTopicDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail(_localizer["taxonomy.topic.nameRequired"]);

        var topic = await _context.Topics.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (topic == null)
            return Fail(_localizer["taxonomy.topic.notFound"]);
        if (!await _context.Subjects.AnyAsync(s => s.Id == dto.SubjectId, ct))
            return Fail(_localizer["taxonomy.subject.invalid"]);
        if (!await _context.Grades.AnyAsync(g => g.Id == dto.GradeId, ct))
            return Fail(_localizer["taxonomy.grade.invalid"]);
        // Only when the topic moves: an already-orphaned topic (pre-#249 data) can still be renamed in place.
        var movesPair = topic.SubjectId != dto.SubjectId || topic.GradeId != dto.GradeId;
        if (movesPair && !await IsSubjectLinkedToGradeAsync(dto.SubjectId, dto.GradeId, ct))
            return Fail(_localizer["taxonomy.topic.subjectNotLinkedToGrade"], TaxonomyErrorCodes.SubjectGradeNotLinked);

        _context.SetCurrentUser(userId);
        topic.Name = name;
        topic.SubjectId = dto.SubjectId;
        topic.GradeId = dto.GradeId;
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.topic.updated"], topic.Id, userId);
    }

    /// <summary>Issue #249: a topic is only reachable under a grade its subject is linked to (active GradeSubject).</summary>
    private Task<bool> IsSubjectLinkedToGradeAsync(int subjectId, int gradeId, CancellationToken ct) =>
        _context.GradeSubjects.AnyAsync(gs => gs.SubjectId == subjectId && gs.GradeId == gradeId, ct);

    public async Task<TaxonomyResponseDto> DeleteTopicAsync(int id, int userId, CancellationToken ct = default)
    {
        var topic = await _context.Topics.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (topic == null)
            return Fail(_localizer["taxonomy.topic.notFound"]);

        if (await _context.SubTopics.AnyAsync(st => st.TopicId == id, ct))
            return Fail(_localizer["taxonomy.topic.hasSubTopics"]);
        if (await _context.Questions.AnyAsync(q => q.TopicId == id, ct))
            return Fail(_localizer["taxonomy.topic.hasQuestions"]);

        _context.SetCurrentUser(userId);
        _context.Topics.Remove(topic);
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.topic.deleted"], id, userId);
    }

    // ---- SubTopic ----

    public async Task<TaxonomyResponseDto> CreateSubTopicAsync(UpsertSubTopicDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail(_localizer["taxonomy.subTopic.nameRequired"]);
        if (!await _context.Topics.AnyAsync(t => t.Id == dto.TopicId, ct))
            return Fail(_localizer["taxonomy.topic.invalid"]);

        _context.SetCurrentUser(userId);
        var subTopic = new SubTopic { Name = name, TopicId = dto.TopicId };
        _context.SubTopics.Add(subTopic);
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.subTopic.created"], subTopic.Id, userId);
    }

    public async Task<TaxonomyResponseDto> UpdateSubTopicAsync(int id, UpsertSubTopicDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail(_localizer["taxonomy.subTopic.nameRequired"]);

        var subTopic = await _context.SubTopics.FirstOrDefaultAsync(st => st.Id == id, ct);
        if (subTopic == null)
            return Fail(_localizer["taxonomy.subTopic.notFound"]);
        if (!await _context.Topics.AnyAsync(t => t.Id == dto.TopicId, ct))
            return Fail(_localizer["taxonomy.topic.invalid"]);

        _context.SetCurrentUser(userId);
        subTopic.Name = name;
        if (subTopic.TopicId != dto.TopicId)
        {
            // issue #61: TopicStudyLink.TopicId, alt konu linklerinde üst konunun denormalize kopyasıdır — alt konu
            // başka konuya taşınınca aynı SaveChanges (tek transaction) içinde güncellenir. Soft-delete edilmiş linkler
            // de (IgnoreQueryFilters) — geri alınırsa tutarsız kalmasınlar.
            var links = await _context.TopicStudyLinks.IgnoreQueryFilters()
                .Where(l => l.SubTopicId == subTopic.Id)
                .ToListAsync(ct);
            foreach (var link in links)
                link.TopicId = dto.TopicId;
        }
        subTopic.TopicId = dto.TopicId;
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.subTopic.updated"], subTopic.Id, userId);
    }

    public async Task<TaxonomyResponseDto> DeleteSubTopicAsync(int id, int userId, CancellationToken ct = default)
    {
        var subTopic = await _context.SubTopics.FirstOrDefaultAsync(st => st.Id == id, ct);
        if (subTopic == null)
            return Fail(_localizer["taxonomy.subTopic.notFound"]);

        if (await _context.QuestionSubTopics.AnyAsync(qst => qst.SubTopicId == id, ct))
            return Fail(_localizer["taxonomy.subTopic.hasQuestions"]);

        _context.SetCurrentUser(userId);
        _context.SubTopics.Remove(subTopic);
        await _context.SaveChangesAsync(ct);
        return Ok(_localizer["taxonomy.subTopic.deleted"], id, userId);
    }

    private static TaxonomyResponseDto Fail(string message, string? errorCode = null) =>
        new() { Success = false, Message = message, ErrorCode = errorCode };

    /// <summary>Success result + a debounced job to rebuild the classifier cache.</summary>
    private TaxonomyResponseDto Ok(string message, int id, int userId)
    {
        _jobs.Schedule<IClassifierCacheService>(s => s.RefreshIfStaleAsync(userId), ReconcileDelay);
        return new TaxonomyResponseDto { Success = true, Message = message, ObjectId = id };
    }
}
