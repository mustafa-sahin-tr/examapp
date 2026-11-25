using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Practice;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.TopicPerformance;

/// <summary>
/// Issue #530: the topics of the student's grade the student answered fewer than <c>minQuestions</c> questions of in the window
/// (the same answers and the same threshold as #497: a topic missing from the report is one of these). Each topic carries how many
/// questions the practice pool (#500) can give in it; a topic with none is <c>Practicable=false</c>. Order: practicable first, then
/// fewest answers, then subject and topic name. A question counts under its topic (a question with only subtopics under the
/// subtopic's topic, as in the report); the pool count uses the question's own topic.
/// </summary>
internal static class UntouchedTopicsQuery
{
    internal static async Task<List<UntouchedTopicDto>> LoadAsync(AppDbContext db, int studentId, int? gradeId, DateTime from, DateTime to,
        int? subjectId, int minQuestions, CancellationToken ct)
    {
        if (gradeId is not { } grade)
            return [];

        var topics = await db.Topics.AsNoTracking()
            .Where(t => t.GradeId == grade && (subjectId == null || t.SubjectId == subjectId))
            .Select(t => new { t.Id, t.Name, t.SubjectId, SubjectName = t.Subject.Name })
            .ToListAsync(ct);
        if (topics.Count == 0)
            return [];

        var answered = (await new StudentAnswerReader(db).ReadAsync(studentId, from, to, ct))
            .Where(a => a.Outcome != StudentAnswerOutcome.Blank && a.Question.EffectiveTopicId is not null)
            .GroupBy(a => a.Question.EffectiveTopicId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var candidates = topics.Where(t => answered.GetValueOrDefault(t.Id) < minQuestions).ToList();
        var candidateIds = candidates.Select(t => t.Id).ToList();
        var available = await PracticeQuestionPool.Build(db, grade, [], candidateIds)
            .GroupBy(q => q.TopicId!.Value)
            .Select(g => new { TopicId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TopicId, x => x.Count, ct);

        return candidates
            .Select(t => new UntouchedTopicDto(t.SubjectId, t.SubjectName, t.Id, t.Name, answered.GetValueOrDefault(t.Id),
                available.GetValueOrDefault(t.Id), available.GetValueOrDefault(t.Id) > 0))
            .OrderByDescending(t => t.Practicable)
            .ThenBy(t => t.AnsweredCount)
            .ThenBy(t => t.SubjectName)
            .ThenBy(t => t.TopicName)
            .ToList();
    }
}
