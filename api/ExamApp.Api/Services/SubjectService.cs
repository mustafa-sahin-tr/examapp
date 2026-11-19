using System;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class SubjectService : ISubjectService
{
    private readonly AppDbContext _context;
    public SubjectService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> DeleteSubjectAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        var subject = await _context.Subjects.FindAsync(subjectId);
        if (subject == null) return false;

        _context.Subjects.Remove(subject);
        return await _context.SaveChangesAsync() > 0;
    }

    public async Task<List<Subject>> GetAllSubjectsAsync(bool coreOnly = false, CancellationToken cancellationToken = default)
    {
        var subjects = _context.Subjects.AsQueryable();
        if (coreOnly)
            subjects = subjects.Where(s => s.Code != null && ProgramService.CoreSubjectCodes.Contains(s.Code));
        return await subjects.ToListAsync(cancellationToken);
    }

    public async Task<List<Topic>> GetTopicBySubjectIdAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        return await _context.Topics.Where(t => t.SubjectId == subjectId).ToListAsync(cancellationToken);
    }

    public async Task<List<SubTopic>> GetSubTopicByTopicIdAsync(int topicId, CancellationToken cancellationToken = default)
    {
        return await _context.SubTopics.Where(st => st.TopicId == topicId).ToListAsync(cancellationToken);
    }

    public async Task<List<Subject>> GetSubjectsByGradeIdAsync(int gradeId, bool coreOnly = false, CancellationToken cancellationToken = default)
    {
        var links = _context.GradeSubjects.Where(gs => gs.GradeId == gradeId);
        if (coreOnly)
        {
            // Issue #1215: katalog sınıfa bütün seçmelileri bağlar; öğrenci seçicileri yalnız ana dersleri gösterir.
            // Sınıfın ana dersi yoksa sınıfın bütün dersleri (ProgramService ile aynı kural, #1213).
            var core = links.Where(gs => gs.Subject.Code != null && ProgramService.CoreSubjectCodes.Contains(gs.Subject.Code));
            if (await core.AnyAsync(cancellationToken))
                links = core;
        }

        return await links
            .Include(gs => gs.Subject)
            .Select(gs => gs.Subject)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Topic>> GetTopicsBySubjectAndGradeAsync(int subjectId, int gradeId, CancellationToken cancellationToken = default)
    {
        return await _context.Topics
            .Where(t => t.SubjectId == subjectId && t.GradeId == gradeId)
            .ToListAsync(cancellationToken);
    }
}
