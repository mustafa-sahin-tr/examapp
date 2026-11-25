using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.TopicPerformance;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// Issue #533: per topic, the student's accuracy next to their grade's. The student's side is read at request time with the same
/// rule as the nightly peer stats (answers in the last <c>PeerWindowDays</c>, blanks out, at least <c>PeerMinAnswers</c> in the
/// topic); the grade's side only from <see cref="TopicAnswerStat"/> (never computed at request time), and only with at least
/// <c>MinStudents</c> students (k-anonymity), else null. Order: the topics furthest behind the grade first, ones without peer data
/// last. Same grade, any school: no school data crosses tenants (#160).
/// </summary>
internal static class TopicPeerComparisonQueries
{
    internal static async Task<TopicPeerComparisonsDto> LoadAsync(AppDbContext context, StudentProfileDto student, int? subjectId,
        HardQuestionOptions options, CancellationToken ct)
    {
        var days = options.PeerWindowDays;
        if (student.GradeId is not { } gradeId)
            return new TopicPeerComparisonsDto(days, options.PeerMinAnswers, null, []);

        var to = DateTime.UtcNow;
        var mine = (await new StudentAnswerReader(context).ReadAsync(student.Id, to.AddDays(-days), to, ct))
            .Where(a => a.Outcome != StudentAnswerOutcome.Blank && a.Question.EffectiveTopicId is not null)
            .Where(a => subjectId is null || a.Question.SubjectId == subjectId)
            .GroupBy(a => a.Question.EffectiveTopicId!.Value)
            .Select(g => (TopicId: g.Key, Answered: g.Count(), Correct: g.Count(a => a.Outcome == StudentAnswerOutcome.Correct)))
            .Where(t => t.Answered >= options.PeerMinAnswers)
            .ToList();
        if (mine.Count == 0)
            return new TopicPeerComparisonsDto(days, options.PeerMinAnswers, null, []);

        var topicIds = mine.Select(t => t.TopicId).ToList();
        var peers = await context.TopicAnswerStats.AsNoTracking()
            .Where(s => s.GradeId == gradeId && topicIds.Contains(s.TopicId) && s.StudentCount >= options.MinStudents)
            .ToDictionaryAsync(s => s.TopicId, ct);
        var computedAt = await context.TopicAnswerStats.AsNoTracking().MaxAsync(s => (DateTime?)s.ComputedAtUtc, ct);
        var topics = await context.Topics.AsNoTracking().IgnoreQueryFilters()
            .Where(t => topicIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Name, t.SubjectId, SubjectName = t.Subject.Name })
            .ToDictionaryAsync(t => t.Id, ct);

        var items = mine
            .Select(t =>
            {
                var topic = topics.GetValueOrDefault(t.TopicId);
                var peer = peers.GetValueOrDefault(t.TopicId);
                return new TopicPeerComparisonDto(
                    topic?.SubjectId, topic?.SubjectName, t.TopicId, topic?.Name, t.Answered,
                    (int)Math.Round(100.0 * t.Correct / t.Answered),
                    peer is null ? null : (int)Math.Round(peer.AccuracyPct),
                    peer is null ? null : (int)Math.Round(peer.MedianPct),
                    peer?.StudentCount);
            })
            .OrderBy(i => i.PeerAccuracyPct is null)
            .ThenBy(i => i.MyAccuracyPct - (i.PeerAccuracyPct ?? 0))
            .ThenBy(i => i.SubjectName).ThenBy(i => i.TopicName)
            .ToList();
        return new TopicPeerComparisonsDto(days, options.PeerMinAnswers, computedAt, items);
    }
}
