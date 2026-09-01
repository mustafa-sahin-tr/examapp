using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Taxonomy;

public class TaxonomyService : ITaxonomyService
{
    private readonly AppDbContext _context;

    public TaxonomyService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TaxonomyTreeDto> GetTreeAsync(CancellationToken ct = default)
    {
        var grades = await _context.Grades
            .OrderBy(g => g.Id)
            .Select(g => new TaxonomyGradeDto { Id = g.Id, Name = g.Name })
            .ToListAsync(ct);

        var subjects = await _context.Subjects.OrderBy(s => s.Name).ToListAsync(ct);
        var topics = await _context.Topics.OrderBy(t => t.Name).ToListAsync(ct);
        var subTopics = await _context.SubTopics.OrderBy(st => st.Name).ToListAsync(ct);

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

        _context.SetCurrentUser(userId);
        var subject = new Subject { Name = name };
        _context.Subjects.Add(subject);
        await _context.SaveChangesAsync(ct);
        return Ok("Ders eklendi.", subject.Id);
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

        _context.SetCurrentUser(userId);
        subject.Name = name;
        await _context.SaveChangesAsync(ct);
        return Ok("Ders güncellendi.", subject.Id);
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
        return Ok("Ders silindi.", id);
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
        return Ok("Konu eklendi.", topic.Id);
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
        return Ok("Konu güncellendi.", topic.Id);
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
        return Ok("Konu silindi.", id);
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
        return Ok("Alt konu eklendi.", subTopic.Id);
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
        return Ok("Alt konu güncellendi.", subTopic.Id);
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
        return Ok("Alt konu silindi.", id);
    }

    private static ResponseBaseDto Fail(string message) => new() { Success = false, Message = message };
    private static ResponseBaseDto Ok(string message, int id) => new() { Success = true, Message = message, ObjectId = id };
}
