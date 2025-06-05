using System;
using ExamApp.Api.Models;

namespace ExamApp.Api.Services.Interfaces;

public interface ISubjectService
{
    Task<bool> DeleteSubjectAsync(int id, CancellationToken cancellationToken = default);
    Task<List<Subject>> GetAllSubjectsAsync(CancellationToken cancellationToken = default);
    Task<List<Subject>> GetSubjectsByGradeIdAsync(int gradeId, CancellationToken cancellationToken = default);

    Task<List<Topic>> GetTopicBySubjectIdAsync(int subjectId, CancellationToken cancellationToken = default);
    Task<List<Topic>> GetTopicsBySubjectAndGradeAsync(int subjectId, int gradeId, CancellationToken cancellationToken = default);

    Task<List<SubTopic>> GetSubTopicByTopicIdAsync(int topicId, CancellationToken cancellationToken = default);
}
