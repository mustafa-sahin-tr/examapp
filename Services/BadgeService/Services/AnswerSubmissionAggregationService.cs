using System;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Entities;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Services;

public class AnswerSubmissionAggregationService
{
    private readonly BadgeDbContext _context;

    public AnswerSubmissionAggregationService(BadgeDbContext context)
    {
        _context = context;
    }

    public async Task ProcessAsync(AnswerSubmittedEvent message, CancellationToken cancellationToken = default)
    {
        var (questionAggregate, previousPoints) = await UpdateStudentQuestionAggregateAsync(message, cancellationToken);
        var subjectAggregate = await UpdateStudentSubjectAggregateAsync(message, cancellationToken);
        var activity = await UpdateDailyActivityAsync(message, cancellationToken);

        // Zaman damgası tüm okumalardan SONRA, SaveChanges'e olabildiğince yakın alınır (issue #225):
        // StudentPointsChangedEvent'in versiyonu budur; aynı kullanıcının event'leri BadgeService'te
        // sıralı işlendiğinden (AnswerSubmittedConsumerDefinition partitioner'ı) versiyon sırası commit
        // sırasıyla örtüşür.
        var now = DateTime.UtcNow;
        questionAggregate.LastUpdatedUtc = now;
        if (subjectAggregate != null)
        {
            subjectAggregate.LastUpdatedUtc = now;
        }
        activity.LastUpdatedUtc = now;

        // Liderlik puan hattı: puan değiştiyse mutlak değeri outbox'a yaz — aggregate ile aynı SaveChanges
        // (aynı transaction). Yanlış cevap puanı değiştirmez → event yok; exam API tarafında satırı olmayan
        // öğrenci zaten 0 XP görünür.
        if (questionAggregate.TotalPoints != previousPoints)
        {
            StudentPointsOutbox.Enqueue(_context, questionAggregate.UserId, questionAggregate.TotalPoints, now);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<(StudentQuestionAggregate Aggregate, int PreviousPoints)> UpdateStudentQuestionAggregateAsync(
        AnswerSubmittedEvent message, CancellationToken cancellationToken)
    {
        var aggregate = await _context.StudentQuestionAggregates
            .FirstOrDefaultAsync(x => x.UserId == message.UserId, cancellationToken);

        if (aggregate == null)
        {
            aggregate = new StudentQuestionAggregate
            {
                Id = Guid.NewGuid(),
                UserId = message.UserId,
            };
            _context.StudentQuestionAggregates.Add(aggregate);
        }

        var previousPoints = aggregate.TotalPoints;

        aggregate.TotalQuestions += 1;
        aggregate.TotalTimeSeconds += Math.Max(0, message.TimeTakenInSeconds);

        if (message.IsCorrect)
        {
            aggregate.CorrectQuestions += 1;
            aggregate.TotalPoints += Math.Max(0, message.QuestionPoint);
            aggregate.CurrentCorrectStreak += 1;
            if (aggregate.CurrentCorrectStreak > aggregate.BestCorrectStreak)
            {
                aggregate.BestCorrectStreak = aggregate.CurrentCorrectStreak;
            }
        }
        else
        {
            aggregate.CurrentCorrectStreak = 0;
        }

        aggregate.LastAnsweredAtUtc = message.SubmittedAt;
        return (aggregate, previousPoints);
    }

    private async Task<StudentSubjectAggregate?> UpdateStudentSubjectAggregateAsync(AnswerSubmittedEvent message, CancellationToken cancellationToken)
    {
        if (!message.SubjectId.HasValue && string.IsNullOrWhiteSpace(message.Subject))
        {
            return null;
        }

        StudentSubjectAggregate? subjectAggregate;

        if (message.SubjectId.HasValue)
        {
            subjectAggregate = await _context.StudentSubjectAggregates
                .FirstOrDefaultAsync(x => x.UserId == message.UserId && x.SubjectId == message.SubjectId, cancellationToken);
        }
        else
        {
            var subjectName = message.Subject ?? string.Empty;
            subjectAggregate = await _context.StudentSubjectAggregates
                .FirstOrDefaultAsync(x => x.UserId == message.UserId && x.SubjectId == null && x.SubjectName == subjectName, cancellationToken);
        }

        if (subjectAggregate == null)
        {
            subjectAggregate = new StudentSubjectAggregate
            {
                Id = Guid.NewGuid(),
                UserId = message.UserId,
                SubjectId = message.SubjectId,
                SubjectName = message.Subject ?? string.Empty,
            };
            _context.StudentSubjectAggregates.Add(subjectAggregate);
        }

        if (!string.IsNullOrWhiteSpace(message.Subject))
        {
            subjectAggregate.SubjectName = message.Subject;
        }

        subjectAggregate.TotalQuestions += 1;
        subjectAggregate.TotalTimeSeconds += Math.Max(0, message.TimeTakenInSeconds);

        if (message.IsCorrect)
        {
            subjectAggregate.CorrectQuestions += 1;
            subjectAggregate.TotalPoints += Math.Max(0, message.QuestionPoint);
        }

        return subjectAggregate;
    }

    private async Task<StudentDailyActivity> UpdateDailyActivityAsync(AnswerSubmittedEvent message, CancellationToken cancellationToken)
    {
        var activityDate = message.SubmittedAt.Date;

        var activity = await _context.StudentDailyActivities
            .FirstOrDefaultAsync(x => x.UserId == message.UserId && x.ActivityDate == activityDate, cancellationToken);

        if (activity == null)
        {
            activity = new StudentDailyActivity
            {
                Id = Guid.NewGuid(),
                UserId = message.UserId,
                ActivityDate = activityDate,
            };
            _context.StudentDailyActivities.Add(activity);
        }

        activity.QuestionCount += 1;
        activity.TotalTimeSeconds += Math.Max(0, message.TimeTakenInSeconds);

        if (message.IsCorrect)
        {
            activity.CorrectCount += 1;
            activity.TotalPoints += Math.Max(0, message.QuestionPoint);
        }

        activity.ActivityScore = CalculateActivityScore(activity);
        return activity;
    }

    private static int CalculateActivityScore(StudentDailyActivity activity)
    {
        // Basit ama genişletilebilir skor hesaplaması.
        // Kullanıcı davranışı çeşitlendikçe ağırlıkları yeniden düzenleyebiliriz.
        var questionScore = activity.QuestionCount * 10;
        var correctBonus = activity.CorrectCount * 5;
        var timeScore = Math.Min(activity.TotalTimeSeconds / 60, 60); // Dakika başına 1 puan, maksimum 60

        return questionScore + correctBonus + timeScore;
    }
}
