using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Classifier;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Taxonomy;

public class TaxonomyService : ITaxonomyService
{
    // Debounce window: a burst of edits schedules several jobs, but only the
    // first finds the cache stale and rebuilds — the rest no-op.
    private static readonly TimeSpan ReconcileDelay = TimeSpan.FromMinutes(2);

    private readonly AppDbContext _context;
    private readonly IBackgroundJobClient _jobs;

    public TaxonomyService(AppDbContext context, IBackgroundJobClient jobs)
    {
        _context = context;
        _jobs = jobs;
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

    public async Task<ResponseBaseDto> CreateSubjectAsync(UpsertSubjectDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Ders adı boş olamaz.");

        if (await _context.Subjects.AnyAsync(s => s.Name.ToLower() == name.ToLower(), ct))
            return Fail("Bu isimde bir ders zaten var.");

        var gradeIds = NormalizeGradeIds(dto.GradeIds);
        if (gradeIds != null && !await AllGradesExistAsync(gradeIds, ct))
            return Fail("Geçersiz sınıf.");

        _context.SetCurrentUser(userId);
        // A brand-new subject has no existing links, so no sync is needed: attach the
        // GradeSubject rows through the navigation and persist subject + links in ONE
        // SaveChanges (single transaction). EF fills SubjectId once the id is generated.
        var subject = new Subject
        {
            Name = name,
            GradeSubjects = (gradeIds ?? new List<int>())
                .Select(gradeId => new GradeSubject { GradeId = gradeId })
                .ToList(),
        };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync(ct);

        return Ok("Ders eklendi.", subject.Id, userId);
    }

    public async Task<ResponseBaseDto> UpdateSubjectAsync(int id, UpsertSubjectDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Ders adı boş olamaz.");

        var subject = await _context.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subject == null)
            return Fail("Ders bulunamadı.");

        if (await _context.Subjects.AnyAsync(s => s.Id != id && s.Name.ToLower() == name.ToLower(), ct))
            return Fail("Bu isimde başka bir ders zaten var.");

        var gradeIds = NormalizeGradeIds(dto.GradeIds);
        if (gradeIds != null && !await AllGradesExistAsync(gradeIds, ct))
            return Fail("Geçersiz sınıf.");

        _context.SetCurrentUser(userId);
        subject.Name = name;
        if (gradeIds != null)
            await SyncSubjectGradesAsync(subject.Id, gradeIds, ct);
        await _context.SaveChangesAsync(ct);
        return Ok("Ders güncellendi.", subject.Id, userId);
    }

    // ---- Subject <-> Grade (GradeSubject) ----

    public async Task<ResponseBaseDto> AddSubjectGradeAsync(int subjectId, int gradeId, int userId, CancellationToken ct = default)
    {
        if (!await _context.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            return Fail("Ders bulunamadı.");
        if (!await _context.Grades.AnyAsync(g => g.Id == gradeId, ct))
            return Fail("Sınıf bulunamadı.");

        if (await _context.GradeSubjects.AnyAsync(gs => gs.SubjectId == subjectId && gs.GradeId == gradeId, ct))
            return Ok("Ders zaten bu sınıfa bağlı.", subjectId, userId);

        _context.SetCurrentUser(userId);
        _context.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
        await _context.SaveChangesAsync(ct);
        return Ok("Ders sınıfa bağlandı.", subjectId, userId);
    }

    public async Task<ResponseBaseDto> RemoveSubjectGradeAsync(int subjectId, int gradeId, int userId, CancellationToken ct = default)
    {
        if (!await _context.Subjects.AnyAsync(s => s.Id == subjectId, ct))
            return Fail("Ders bulunamadı.");
        if (!await _context.Grades.AnyAsync(g => g.Id == gradeId, ct))
            return Fail("Sınıf bulunamadı.");

        var links = await _context.GradeSubjects
            .Where(gs => gs.SubjectId == subjectId && gs.GradeId == gradeId)
            .ToListAsync(ct);
        if (links.Count == 0)
            return Ok("Ders zaten bu sınıfa bağlı değil.", subjectId, userId);

        // Only the link row goes (soft delete via ApplyAuditInfo); topics/subtopics/questions stay.
        _context.SetCurrentUser(userId);
        _context.GradeSubjects.RemoveRange(links);
        await _context.SaveChangesAsync(ct);
        return Ok("Ders–sınıf bağlantısı kaldırıldı.", subjectId, userId);
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
    /// Makes the subject's GradeSubject links equal to <paramref name="desiredGradeIds"/>:
    /// missing links are added, links not in the list are removed. Caller saves.
    /// </summary>
    private async Task SyncSubjectGradesAsync(int subjectId, List<int> desiredGradeIds, CancellationToken ct)
    {
        var existing = await _context.GradeSubjects
            .Where(gs => gs.SubjectId == subjectId)
            .ToListAsync(ct);

        var existingGradeIds = existing.Select(gs => gs.GradeId).ToHashSet();
        var desired = desiredGradeIds.ToHashSet();

        var toRemove = existing.Where(gs => !desired.Contains(gs.GradeId)).ToList();
        if (toRemove.Count > 0)
            _context.GradeSubjects.RemoveRange(toRemove);

        foreach (var gradeId in desired.Where(g => !existingGradeIds.Contains(g)))
            _context.GradeSubjects.Add(new GradeSubject { SubjectId = subjectId, GradeId = gradeId });
    }

    public async Task<ResponseBaseDto> DeleteSubjectAsync(int id, int userId, CancellationToken ct = default)
    {
        var subject = await _context.Subjects.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (subject == null)
            return Fail("Ders bulunamadı.");

        if (await _context.Topics.AnyAsync(t => t.SubjectId == id, ct))
            return Fail("Bu derse bağlı konular var. Önce konuları silin veya taşıyın.");

        if (await _context.Questions.AnyAsync(q => q.SubjectId == id, ct))
            return Fail("Bu derse bağlı sorular var, silinemez.");

        _context.SetCurrentUser(userId);
        _context.Subjects.Remove(subject); // soft delete via AppDbContext.ApplyAuditInfo
        await _context.SaveChangesAsync(ct);
        return Ok("Ders silindi.", id, userId);
    }

    // ---- Topic ----

    public async Task<ResponseBaseDto> CreateTopicAsync(UpsertTopicDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Konu adı boş olamaz.");
        if (!await _context.Subjects.AnyAsync(s => s.Id == dto.SubjectId, ct))
            return Fail("Geçersiz ders.");
        if (!await _context.Grades.AnyAsync(g => g.Id == dto.GradeId, ct))
            return Fail("Geçersiz sınıf.");

        _context.SetCurrentUser(userId);
        var topic = new Topic { Name = name, SubjectId = dto.SubjectId, GradeId = dto.GradeId };
        _context.Topics.Add(topic);
        await _context.SaveChangesAsync(ct);
        return Ok("Konu eklendi.", topic.Id, userId);
    }

    public async Task<ResponseBaseDto> UpdateTopicAsync(int id, UpsertTopicDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Konu adı boş olamaz.");

        var topic = await _context.Topics.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (topic == null)
            return Fail("Konu bulunamadı.");
        if (!await _context.Subjects.AnyAsync(s => s.Id == dto.SubjectId, ct))
            return Fail("Geçersiz ders.");
        if (!await _context.Grades.AnyAsync(g => g.Id == dto.GradeId, ct))
            return Fail("Geçersiz sınıf.");

        _context.SetCurrentUser(userId);
        topic.Name = name;
        topic.SubjectId = dto.SubjectId;
        topic.GradeId = dto.GradeId;
        await _context.SaveChangesAsync(ct);
        return Ok("Konu güncellendi.", topic.Id, userId);
    }

    public async Task<ResponseBaseDto> DeleteTopicAsync(int id, int userId, CancellationToken ct = default)
    {
        var topic = await _context.Topics.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (topic == null)
            return Fail("Konu bulunamadı.");

        if (await _context.SubTopics.AnyAsync(st => st.TopicId == id, ct))
            return Fail("Bu konuya bağlı alt konular var. Önce onları silin veya taşıyın.");
        if (await _context.Questions.AnyAsync(q => q.TopicId == id, ct))
            return Fail("Bu konuya bağlı sorular var, silinemez.");

        _context.SetCurrentUser(userId);
        _context.Topics.Remove(topic);
        await _context.SaveChangesAsync(ct);
        return Ok("Konu silindi.", id, userId);
    }

    // ---- SubTopic ----

    public async Task<ResponseBaseDto> CreateSubTopicAsync(UpsertSubTopicDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Alt konu adı boş olamaz.");
        if (!await _context.Topics.AnyAsync(t => t.Id == dto.TopicId, ct))
            return Fail("Geçersiz konu.");

        _context.SetCurrentUser(userId);
        var subTopic = new SubTopic { Name = name, TopicId = dto.TopicId };
        _context.SubTopics.Add(subTopic);
        await _context.SaveChangesAsync(ct);
        return Ok("Alt konu eklendi.", subTopic.Id, userId);
    }

    public async Task<ResponseBaseDto> UpdateSubTopicAsync(int id, UpsertSubTopicDto dto, int userId, CancellationToken ct = default)
    {
        var name = dto.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return Fail("Alt konu adı boş olamaz.");

        var subTopic = await _context.SubTopics.FirstOrDefaultAsync(st => st.Id == id, ct);
        if (subTopic == null)
            return Fail("Alt konu bulunamadı.");
        if (!await _context.Topics.AnyAsync(t => t.Id == dto.TopicId, ct))
            return Fail("Geçersiz konu.");

        _context.SetCurrentUser(userId);
        subTopic.Name = name;
        subTopic.TopicId = dto.TopicId;
        await _context.SaveChangesAsync(ct);
        return Ok("Alt konu güncellendi.", subTopic.Id, userId);
    }

    public async Task<ResponseBaseDto> DeleteSubTopicAsync(int id, int userId, CancellationToken ct = default)
    {
        var subTopic = await _context.SubTopics.FirstOrDefaultAsync(st => st.Id == id, ct);
        if (subTopic == null)
            return Fail("Alt konu bulunamadı.");

        if (await _context.QuestionSubTopics.AnyAsync(qst => qst.SubTopicId == id, ct))
            return Fail("Bu alt konuya atanmış sorular var, silinemez.");

        _context.SetCurrentUser(userId);
        _context.SubTopics.Remove(subTopic);
        await _context.SaveChangesAsync(ct);
        return Ok("Alt konu silindi.", id, userId);
    }

    private static ResponseBaseDto Fail(string message) => new() { Success = false, Message = message };

    /// <summary>Success result + a debounced job to rebuild the classifier cache.</summary>
    private ResponseBaseDto Ok(string message, int id, int userId)
    {
        _jobs.Schedule<IClassifierCacheService>(s => s.RefreshIfStaleAsync(userId), ReconcileDelay);
        return new ResponseBaseDto { Success = true, Message = message, ObjectId = id };
    }
}
