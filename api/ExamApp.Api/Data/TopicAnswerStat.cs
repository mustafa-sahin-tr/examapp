namespace ExamApp.Api.Data;

/// <summary>
/// Issue #533: how a grade's students do in a topic, precomputed nightly by <see cref="Services.Practice.QuestionAnswerStatsJob"/>.
/// Over the students with at least <c>HardQuestions:PeerMinAnswers</c> answers in the topic in the last
/// <c>HardQuestions:PeerWindowDays</c> days: their count and the mean and median of their accuracy. An aggregate only; read
/// only when <see cref="StudentCount"/> is at least <c>HardQuestions:MinStudents</c> (k-anonymity). No student is stored here.
/// </summary>
public class TopicAnswerStat
{
    public int GradeId { get; set; }

    public int TopicId { get; set; }

    public int StudentCount { get; set; }

    /// <summary>Mean of the students' accuracy, 0–100.</summary>
    public double AccuracyPct { get; set; }

    /// <summary>Median of the students' accuracy, 0–100.</summary>
    public double MedianPct { get; set; }

    public DateTime ComputedAtUtc { get; set; }
}
