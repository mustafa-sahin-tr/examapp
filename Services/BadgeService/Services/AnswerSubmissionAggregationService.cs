using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Entities;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BadgeService.Services;

public class AnswerSubmissionAggregationService
{
    private readonly BadgeDbContext _context;
    private readonly AnswerPointOptions _pointOptions;
    private readonly ILogger<AnswerSubmissionAggregationService> _logger;

    public AnswerSubmissionAggregationService(
        BadgeDbContext context,
        IOptions<AnswerPointOptions>? pointOptions = null,
        ILogger<AnswerSubmissionAggregationService>? logger = null)
    {
        _context = context;
        _pointOptions = pointOptions?.Value ?? new AnswerPointOptions();
        _logger = logger ?? NullLogger<AnswerSubmissionAggregationService>.Instance;
    }

    /// <summary>
    /// Aggregate'i günceller ve puan değiştiyse <see cref="StudentPointsOutbox"/>'a yazar.
    ///
    /// Idempotency katman 1 (issue #243): <paramref name="message"/>.EventId dolu (≠ <see cref="Guid.Empty"/>)
    /// ise, aynı EventId ile daha önce işlenmişse hiçbir şey yapmadan <c>false</c> döner (bkz.
    /// <see cref="ProcessedAnswerSubmission"/>) — aynı SaveChanges'te bir ledger satırı eklenir, eşzamanlı
    /// ikinci teslim PK ihlaline (23505) çarpar ve no-op sayılır.
    ///
    /// Idempotency katman 2 / çift puan düzeltmesi (issue #279, item 4 — ürün kararı "soru başına bir kez,
    /// son cevap sayılır"): (TestInstanceId, QuestionId) başına en son uygulanan puan ve revizyon
    /// <see cref="AnswerPointAward"/>'da tutulur. Revizyon KAYNAĞI (issue #279 review, blocker — güncellendi):
    /// BİRİNCİL olarak <c>AnswerSubmittedEvent.Revision</c> (exam API'de <c>WorksheetInstanceQuestion.AnswerRevision</c>,
    /// DB tarafında atomik artan bir sayaç — istemci saatine bağlı değil, iki SaveAnswer çağrısı için asla
    /// aynı değeri üretmez). Revision &gt; 0 ise karşılaştırma TAM SAYI olarak yapılır: mesaj &lt;= kayıttaki
    /// değer ⇒ sırasız/tekrar teslim. Revision == 0 (yalnızca bu alandan ÖNCE üretilmiş/kuyrukta kalmış eski
    /// mesajlar) ise YEDEK olarak <c>SubmittedAt</c> (mikrosaniyeye yuvarlanmış, <c>EventVersion.Normalize</c>)
    /// kullanılır — VE gelecekte >5 dk olan bir SubmittedAt reddedilir (log + atla): istemci saat kaymasının ya
    /// da sahte bir mesajın revizyon korumasını kalıcı olarak kilitlemesini önler. Gelen mesaj (hangi modla
    /// olursa olsun) stale ise TÜM aggregate güncellemesi atlanır (yalnızca puan değil — bkz.
    /// <see cref="ResolvePointAwardAsync"/>). Kullanıcı uyuşmazlığı (issue #279 review): kayıttaki
    /// <see cref="AnswerPointAward.UserId"/> mesajınkiyle eşleşmiyorsa mesaj reddedilir/loglanır, hiçbir
    /// güncelleme yapılmaz (savunma amaçlı — normalde TestInstanceId zaten tek öğrenciye bağlıdır).
    ///
    /// Stale değilse yeni puan (yanlışsa 0, doğruysa cap'lenmiş QuestionPoint) ile önceki uygulanan puan
    /// arasındaki FARK (delta, negatif olabilir) aggregate'lere uygulanır — böylece doğru→yanlış→doğru gibi
    /// cevap değişiklikleri puanı katlamak yerine yalnızca son cevabı yansıtır. Bu katman EventId'den
    /// bağımsızdır: Guid.Empty (eski üretici) mesajları da SubmittedAt/Revision doluysa korunur.
    ///
    /// Sayaç semantiği (issue #785): TotalQuestions/CorrectQuestions/TotalTimeSeconds/QuestionCount/CorrectCount
    /// de puan gibi (TestInstanceId, QuestionId) başına son cevabı yansıtır; fark <see cref="AnswerPointAward"/>'daki
    /// sayılmış durumdan hesaplanır ve sorunun ilk sayıldığı günün aktivite satırına uygulanır. Temizlenen cevap
    /// (<see cref="AnswerSubmittedEvent.IsCleared"/>) sayaçlardan çıkar ve yanlış sayılmaz.
    ///
    /// Seri kuralı (issue #785): seriyi yalnız sorunun oturumdaki İLK doğru cevabı artırır, cevabı doğruya çevirmek
    /// veya temizleyip yeniden doğru vermek artırmaz. Yanlış cevap seriyi sıfırlar; soru zaten yanlış sayılıyorsa
    /// (yanlıştan yanlışa değişiklik) sıfırlamaz. Temizleme seriye dokunmaz. BestCorrectStreak geri düşmez.
    ///
    /// Yatay ölçekleme (issue #279, item 1): tüm işlem <see cref="ConcurrencyRetry"/> ile sarılır — birden
    /// fazla BadgeService instance'ı aynı aggregate satırını eşzamanlı güncellerse xmin concurrency token'ı
    /// (bkz. <c>BadgeDbContext</c>) <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
    /// fırlatır; bu sınırlı sayıda (varsayılan 3) yeniden denenir, her denemeden önce ChangeTracker temizlenir
    /// (taze okuma). Tükenirse istisna yukarı fırlar → consumer'ın retry/dead-letter yoluna girer.
    /// </summary>
    /// <returns><c>true</c> aggregate yeni uygulandıysa; <c>false</c> duplicate/sırasız olarak atlandıysa.</returns>
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

        return await ConcurrencyRetry.ExecuteAsync(
            () => ProcessOnceAsync(message, dedupe, cancellationToken),
            onRetry: (attempt, ex) =>
            {
                _logger.LogWarning(ex,
                    "[AnswerSubmission] Concurrency çakışması (deneme {Attempt}), UserId={UserId}, TestInstanceId={TestInstanceId}, QuestionId={QuestionId}; yeniden deneniyor.",
                    attempt, message.UserId, message.TestInstanceId, message.QuestionId);
                _context.ChangeTracker.Clear();
            });
    }

    private async Task<bool> ProcessOnceAsync(AnswerSubmittedEvent message, bool dedupe, CancellationToken cancellationToken)
    {
        // issue #396: kullanıcı bu cevap gönderildikten SONRA sıfırlandıysa (UserResetService) event yok sayılır —
        // sıfırlama AnswerPointAward/ProcessedAnswerSubmission'ı sildiği için aksi halde soft-delete edilmiş instance'ın
        // puanı yeniden verilirdi. Concurrency retry'ında da yeniden okunur (sıfırlama arada commit etmiş olabilir).
        if (await IsBeforeUserResetAsync(message, cancellationToken))
        {
            return false;
        }

        var award = await ResolvePointAwardAsync(message, cancellationToken);
        if (award.IsStale)
        {
            // Sırasız/tekrar teslim (issue #279 item 4): hiçbir aggregate güncellenmedi, evaluator da
            // çalıştırılmamalı — çağıran (AnswerSubmittedConsumer) false döndüğünde bunu atlar.
            return false;
        }

        // issue #785: hiç sayılmamış sorunun temizlenmesi: yalnız revizyon kaydedilir, sayaç ve puan değişmez.
        if (award.Counts.ActivityDate is not { } activityDate)
        {
            AddProcessedLedgerEntry(message, dedupe, DateTime.UtcNow);
            return await SaveAsync(dedupe, cancellationToken);
        }

        var (questionAggregate, previousPoints) = await UpdateStudentQuestionAggregateAsync(message, award.Delta, award.Counts, cancellationToken);
        var subjectAggregate = await UpdateStudentSubjectAggregateAsync(message, award.Delta, award.Counts, cancellationToken);
        var activity = await UpdateDailyActivityAsync(message, award.Delta, award.Counts, activityDate, cancellationToken);

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

        AddProcessedLedgerEntry(message, dedupe, now);
        return await SaveAsync(dedupe, cancellationToken);
    }

    private void AddProcessedLedgerEntry(AnswerSubmittedEvent message, bool dedupe, DateTime now)
    {
        if (dedupe)
        {
            _context.ProcessedAnswerSubmissions.Add(new ProcessedAnswerSubmission
            {
                EventId = message.EventId,
                UserId = message.UserId,
                ProcessedAt = now,
            });
        }
    }

    private async Task<bool> SaveAsync(bool dedupe, CancellationToken cancellationToken)
    {
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
            // StudentDailyActivity/AnswerPointAward) unique index ihlalleri BURADA yakalanmaz, yukarı fırlar
            // (review fix, issue #243): aksi halde eşzamanlı ama GERÇEKTEN FARKLI bir cevabın puanı sessizce
            // kaybolurdu.
            return false;
        }

        return true;
    }

    /// <summary>
    /// issue #396: <paramref name="message"/>'ın <c>SubmittedAt</c>'ı kullanıcının son sıfırlamasından
    /// (<see cref="UserResetMarker.ResetAtUtc"/>) önceyse true. Sıfırlama kaydı yoksa false.
    /// </summary>
    private async Task<bool> IsBeforeUserResetAsync(AnswerSubmittedEvent message, CancellationToken cancellationToken)
    {
        var resetAt = await _context.UserResetMarkers
            .AsNoTracking()
            .Where(x => x.UserId == message.UserId)
            .Select(x => (DateTime?)x.ResetAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (resetAt is null)
        {
            return false;
        }

        var submittedAt = EventVersion.Normalize(message.SubmittedAt);
        if (submittedAt >= EventVersion.Normalize(resetAt.Value))
        {
            return false;
        }

        _logger.LogInformation(
            "[AnswerSubmission] Sıfırlama öncesi gönderilmiş cevap yok sayıldı (UserId={UserId}, TestInstanceId={TestInstanceId}, " +
            "QuestionId={QuestionId}, SubmittedAt={SubmittedAt:o}, ResetAt={ResetAt:o}).",
            message.UserId, message.TestInstanceId, message.QuestionId, submittedAt, resetAt.Value);
        return true;
    }

    private readonly record struct PointAwardResolution(bool IsStale, int Delta, AnswerCountChange Counts = default);

    /// <summary>
    /// issue #785: bu mesajın sayaçlara uygulayacağı fark. <see cref="ActivityDate"/>: farkın yazılacağı aktivite günü;
    /// null ise soru hiç sayılmadı ve sayılmıyor (temizleme), sayaçlara dokunulmaz.
    /// </summary>
    private readonly record struct AnswerCountChange(
        int Questions, int Correct, int TimeSeconds, DateTime? ActivityDate, bool RaisesStreak, bool ResetsStreak);

    /// <summary>
    /// (TestInstanceId, QuestionId) başına puan tekilleştirmesi — bkz. <see cref="ProcessAsync"/> XML doc ve
    /// <see cref="AnswerPointAward"/>. Cap (issue #279 item 6): sahte/bozuk bir <c>QuestionPoint</c> ile puan
    /// şişirmeyi önlemek için <see cref="AnswerPointOptions.MaxQuestionPoint"/>'e kırpılır (reddedilmez —
    /// cevabın kendisi geçerli olabilir; yalnızca puanı sınırlanır), Warning loglanır.
    /// </summary>
    private async Task<PointAwardResolution> ResolvePointAwardAsync(AnswerSubmittedEvent message, CancellationToken cancellationToken)
    {
        var normalizedSubmittedAt = EventVersion.Normalize(message.SubmittedAt);
        var award = await _context.AnswerPointAwards
            .FirstOrDefaultAsync(
                a => a.TestInstanceId == message.TestInstanceId && a.QuestionId == message.QuestionId,
                cancellationToken);

        // issue #279 review (blocker): kullanıcı uyuşmazlığı — bu (TestInstanceId, QuestionId) kaydı
        // başka bir kullanıcıya ait. Normalde imkânsız (TestInstanceId tek öğrenciye bağlı) ama savunma
        // amaçlı: reddet/logla, HİÇBİR güncelleme yapma.
        if (award != null && award.UserId != message.UserId)
        {
            _logger.LogWarning(
                "[AnswerSubmission] UserId uyuşmazlığı — (TestInstanceId={TestInstanceId}, QuestionId={QuestionId}) " +
                "AwardUserId={AwardUserId} kaydına ait, mesaj UserId={MessageUserId}; mesaj reddedildi.",
                message.TestInstanceId, message.QuestionId, award.UserId, message.UserId);
            return new PointAwardResolution(true, 0);
        }

        bool isStale;
        if (message.Revision > 0)
        {
            // Birincil yol: DB tarafında atomik artan tam sayı revizyon (bkz. AnswerPointAward XML doc).
            isStale = award != null && message.Revision <= award.LastAppliedRevision;
        }
        else
        {
            // Yedek yol (Revision=0 — #279'dan önceki/kuyrukta kalmış eski mesajlar): SubmittedAt.
            var now = DateTime.UtcNow;
            if (normalizedSubmittedAt > now.AddMinutes(5))
            {
                _logger.LogWarning(
                    "[AnswerSubmission] Revision yok ve SubmittedAt gelecekte (>5dk) — mesaj reddedildi " +
                    "(UserId={UserId}, TestInstanceId={TestInstanceId}, QuestionId={QuestionId}, SubmittedAt={SubmittedAt:o}).",
                    message.UserId, message.TestInstanceId, message.QuestionId, normalizedSubmittedAt);
                return new PointAwardResolution(true, 0);
            }
            isStale = award != null && normalizedSubmittedAt <= award.LastAppliedRevisionUtc;
        }

        if (isStale)
        {
            return new PointAwardResolution(true, 0);
        }

        var rawPoint = Math.Max(0, message.QuestionPoint);
        if (rawPoint > _pointOptions.MaxQuestionPoint)
        {
            _logger.LogWarning(
                "[AnswerSubmission] QuestionPoint üst sınırı aşıyor; kırpıldı (UserId={UserId}, QuestionId={QuestionId}, QuestionPoint={QuestionPoint}, Max={Max}).",
                message.UserId, message.QuestionId, message.QuestionPoint, _pointOptions.MaxQuestionPoint);
        }
        var cappedPoint = Math.Min(rawPoint, _pointOptions.MaxQuestionPoint);

        var isCorrect = message.IsCorrect && !message.IsCleared;
        var newPoints = isCorrect ? cappedPoint : 0;
        var previousAwarded = award?.PointsAwarded ?? 0;
        var delta = newPoints - previousAwarded;
        var counts = ResolveCountChange(award, message, isCorrect);

        if (award == null)
        {
            award = new AnswerPointAward
            {
                TestInstanceId = message.TestInstanceId,
                QuestionId = message.QuestionId,
                UserId = message.UserId,
            };
            _context.AnswerPointAwards.Add(award);
        }

        award.TestInstanceQuestionId = message.TestInstanceQuestionId;
        award.PointsAwarded = newPoints;
        // LastAppliedRevisionUtc her zaman güncellenir (denetim/yedek kıyas); LastAppliedRevision yalnızca
        // mesaj gerçek bir Revision taşıyorsa güncellenir — 0 ile ezilirse, revizyon izleyen bir kayıt daha
        // sonra gelen eski-formatlı (Revision=0) bir mesajla geriye sıfırlanırdı (bkz. AnswerPointAward XML doc).
        if (message.Revision > 0)
        {
            award.LastAppliedRevision = message.Revision;
        }
        award.LastAppliedRevisionUtc = normalizedSubmittedAt;
        award.UpdatedAtUtc = DateTime.UtcNow;

        award.CountedActivityDate = message.IsCleared ? null : counts.ActivityDate;
        award.CountedCorrect = isCorrect;
        award.CountedTimeSeconds = message.IsCleared ? 0 : Math.Max(0, message.TimeTakenInSeconds);
        award.HasBeenAnswered |= !message.IsCleared;

        return new PointAwardResolution(false, delta, counts);
    }

    /// <summary>issue #785: sayılmış durumdan (award) yeni cevaba geçişin farkı; kurallar için bkz. <see cref="ProcessAsync"/>.</summary>
    private static AnswerCountChange ResolveCountChange(AnswerPointAward? award, AnswerSubmittedEvent message, bool isCorrect)
    {
        var countedDate = award?.CountedActivityDate;
        var wasCounted = countedDate != null;
        var wasCorrect = award?.CountedCorrect == true;
        var isCounted = !message.IsCleared;
        var newTime = isCounted ? Math.Max(0, message.TimeTakenInSeconds) : 0;

        return new AnswerCountChange(
            Questions: (isCounted ? 1 : 0) - (wasCounted ? 1 : 0),
            Correct: (isCorrect ? 1 : 0) - (wasCorrect ? 1 : 0),
            TimeSeconds: newTime - (award?.CountedTimeSeconds ?? 0),
            ActivityDate: countedDate ?? (isCounted ? message.SubmittedAt.Date : null),
            RaisesStreak: isCorrect && award?.HasBeenAnswered != true,
            ResetsStreak: isCounted && !isCorrect && (!wasCounted || wasCorrect));
    }

    /// <summary>
    /// Only the ledger's own PK (<c>PK_ProcessedAnswerSubmissions</c>) is treated as a duplicate-delivery
    /// no-op (review fix, issue #243). Without the constraint-name check, a 23505 on any of the OTHER
    /// unique indexes hit by the same SaveChanges — <c>IX_StudentQuestionAggregates_UserId</c>,
    /// <c>IX_StudentSubjectAggregates_UserId_SubjectId</c>, <c>IX_StudentDailyActivities_UserId_ActivityDate</c>,
    /// <c>PK_AnswerPointAwards</c> (all from concurrent, GENUINELY DIFFERENT answers for the same user racing
    /// on the same aggregate row — the partitioner normally serializes this per user, but a second BadgeService
    /// instance or a direct call could still race) — would be silently swallowed as "duplicate", and that
    /// legitimate answer's points would vanish with no error, no retry, nothing in the dead-letter queue.
    /// </summary>
    private const string ProcessedAnswerSubmissionPrimaryKeyConstraint = "PK_ProcessedAnswerSubmissions";

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException
        {
            SqlState: "23505",
            ConstraintName: ProcessedAnswerSubmissionPrimaryKeyConstraint
        };

    private async Task<(StudentQuestionAggregate Aggregate, int PreviousPoints)> UpdateStudentQuestionAggregateAsync(
        AnswerSubmittedEvent message, int pointsDelta, AnswerCountChange counts, CancellationToken cancellationToken)
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

        aggregate.TotalQuestions = Math.Max(0, aggregate.TotalQuestions + counts.Questions);
        aggregate.CorrectQuestions = Math.Max(0, aggregate.CorrectQuestions + counts.Correct);
        aggregate.TotalTimeSeconds = Math.Max(0, aggregate.TotalTimeSeconds + counts.TimeSeconds);

        if (counts.RaisesStreak)
        {
            aggregate.CurrentCorrectStreak += 1;
            if (aggregate.CurrentCorrectStreak > aggregate.BestCorrectStreak)
            {
                aggregate.BestCorrectStreak = aggregate.CurrentCorrectStreak;
            }
        }
        else if (counts.ResetsStreak)
        {
            aggregate.CurrentCorrectStreak = 0;
        }

        // issue #279 item 4: mutlak ekleme değil, (TestInstanceId, QuestionId) başına delta uygulanır.
        aggregate.TotalPoints = Math.Max(0, aggregate.TotalPoints + pointsDelta);

        if (!message.IsCleared)
        {
            aggregate.LastAnsweredAtUtc = message.SubmittedAt;
        }
        return (aggregate, previousPoints);
    }

    private async Task<StudentSubjectAggregate?> UpdateStudentSubjectAggregateAsync(AnswerSubmittedEvent message, int pointsDelta, AnswerCountChange counts, CancellationToken cancellationToken)
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

        subjectAggregate.TotalQuestions = Math.Max(0, subjectAggregate.TotalQuestions + counts.Questions);
        subjectAggregate.CorrectQuestions = Math.Max(0, subjectAggregate.CorrectQuestions + counts.Correct);
        subjectAggregate.TotalTimeSeconds = Math.Max(0, subjectAggregate.TotalTimeSeconds + counts.TimeSeconds);

        subjectAggregate.TotalPoints = Math.Max(0, subjectAggregate.TotalPoints + pointsDelta);

        return subjectAggregate;
    }

    private async Task<StudentDailyActivity> UpdateDailyActivityAsync(
        AnswerSubmittedEvent message, int pointsDelta, AnswerCountChange counts, DateTime activityDate, CancellationToken cancellationToken)
    {
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

        activity.QuestionCount = Math.Max(0, activity.QuestionCount + counts.Questions);
        activity.CorrectCount = Math.Max(0, activity.CorrectCount + counts.Correct);
        activity.TotalTimeSeconds = Math.Max(0, activity.TotalTimeSeconds + counts.TimeSeconds);

        // issue #785: cevap değişikliği yeni bir aktivite günü açmaz; sayaç ve puan farkı sorunun sayıldığı güne yazılır.
        activity.TotalPoints = Math.Max(0, activity.TotalPoints + pointsDelta);

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
