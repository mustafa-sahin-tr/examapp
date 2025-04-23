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

    public async Task<bool> DeleteSubjectAsync(int subjectId,CancellationToken cancellationToken = default)
    {
        var subject = await _context.Subjects.FindAsync(subjectId);
        if (subject == null) return false;

        _context.Subjects.Remove(subject);
        return await _context.SaveChangesAsync() > 0;
    }

    public async Task<List<Subject>> GetAllSubjectsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Subjects.ToListAsync();
    }

    public async Task<List<Topic>> GetTopicBySubjectIdAsync(int subjectId, CancellationToken cancellationToken = default)
    {
        return await _context.Topics.Where(t => t.SubjectId == subjectId).ToListAsync(cancellationToken);
    }

    public async Task<List<SubTopic>> GetSubTopicByTopicIdAsync(int topicId, CancellationToken cancellationToken = default)
    {
        return await _context.SubTopics.Where(st => st.TopicId == topicId).ToListAsync(cancellationToken);
    }
    
}
