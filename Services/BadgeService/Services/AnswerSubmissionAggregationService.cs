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

    /// <summary>
    /// Aggregate'i günceller ve puan değiştiyse <see cref="StudentPointsOutbox"/>'a yazar.
    /// Idempotency (issue #243): <paramref name="message"/>.EventId dolu (≠ <see cref="Guid.Empty"/>)
    /// ise, aynı EventId ile daha önce işlenmişse hiçbir şey yapmadan <c>false</c> döner; aksi halde
    /// aggregate güncellemesiyle AYNI SaveChanges'te bir <see cref="ProcessedAnswerSubmission"/> satırı
    /// eklenir. Eşzamanlı ikinci teslim PK ihlaline (23505) çarpar ve no-op sayılır. EventId boşsa
    /// (eski üreticiler) dedup tamamen atlanır — tekrar teslimde aggregate yine güncellenir (önceki
    /// davranış). Dönüş değeri, çağıranın (bkz. <c>AnswerSubmittedConsumer</c>) badge/streak
    /// değerlendirmesini atlayıp atlamayacağını belirler: duplicate olduğunda evaluator da atlanır.
    /// </summary>
    /// <returns><c>true</c> aggregate yeni uygulandıysa; <c>false</c> duplicate olarak atlandıysa.</returns>
    public async Task<bool> ProcessAsync(AnswerSubmittedEvent message, CancellationToken cancellationToken = default)
    {
        var dedupe = message.EventId != Guid.Empty;
        if (dedupe)
        {
            var alreadyProcessed = await _context.ProcessedAnswerSubmissions
                .AnyAsync(p => p.EventId == message.EventId, cancellationToken);
            if (alreadyProcessed)
            {
                return false;
            }
        }

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

        if (dedupe)
        {
            _context.ProcessedAnswerSubmissions.Add(new ProcessedAnswerSubmission
            {
                EventId = message.EventId,
                UserId = message.UserId,
                ProcessedAt = now,
            });
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (dedupe && IsUniqueViolation(ex))
        {
            // Eşzamanlı ikinci teslim aynı EventId'yle ledger PK'sına (PK_ProcessedAnswerSubmissions)
            // çarptı — aggregate zaten (ya da az önce) başka bir çağrıda uygulandı. Evaluator da
            // atlanmalı; çağıran false döndüğünde bunu yapar. DİKKAT: IsUniqueViolation constraint adını
            // da kontrol eder — aggregate tablolarındaki (StudentQuestionAggregate/StudentSubjectAggregate/
            // StudentDailyActivity) unique index ihlalleri BURADA yakalanmaz, yukarı fırlar (review fix,
            // issue #243): aksi halde eşzamanlı ama GERÇEKTEN FARKLI bir cevabın puanı sessizce kaybolurdu.
            return false;
        }

        return true;
    }

    /// <summary>
    /// Only the ledger's own PK (<c>PK_ProcessedAnswerSubmissions</c>) is treated as a duplicate-delivery
    /// no-op (review fix, issue #243). Without the constraint-name check, a 23505 on any of the OTHER
    /// unique indexes hit by the same SaveChanges — <c>IX_StudentQuestionAggregates_UserId</c>,
    /// <c>IX_StudentSubjectAggregates_UserId_SubjectId</c>, <c>IX_StudentDailyActivities_UserId_ActivityDate</c>
    /// (all from concurrent, GENUINELY DIFFERENT answers for the same user racing on the same aggregate
    /// row — the partitioner normally serializes this per user, but a second BadgeService instance or a
    /// direct call could still race) — would be silently swallowed as "duplicate", and that legitimate
    /// answer's points would vanish with no error, no retry, nothing in the dead-letter queue.
    /// </summary>
    private const string ProcessedAnswerSubmissionPrimaryKeyConstraint = "PK_ProcessedAnswerSubmissions";

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException
        {
            SqlState: "23505",
            ConstraintName: ProcessedAnswerSubmissionPrimaryKeyConstraint
        };

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
