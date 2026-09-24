using System;
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
    /// Sayaç semantiği (issue #279 review — netleştirme): yalnızca PUAN (TotalPoints alanları) "son cevap
    /// sayılır" kuralına tabidir. TotalQuestions/CorrectQuestions/CurrentCorrectStreak/BestCorrectStreak/
    /// QuestionCount/CorrectCount gibi diğer TÜM sayaçlar her stale-olmayan mesajda hâlâ artırılır — yani
    /// bunların nihai değeri MESAJLARIN VARIŞ SIRASINA bağlıdır (ör. sırasız teslim edilip sonradan stale
    /// sayılan bir mesaj için sayaçlar da atlanır, ama normal sırayla gelen her farklı revizyon ayrı bir
    /// "deneme" olarak sayılmaya devam eder). Bu kasıtlı: issue #279 item 4'ün kapsamı yalnızca çift puan,
    /// deneme/streak sayaçlarının "son cevap" kuralına tabi tutulması ayrı bir ürün kararı gerektirir.
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
        var award = await ResolvePointAwardAsync(message, cancellationToken);
        if (award.IsStale)
        {
            // Sırasız/tekrar teslim (issue #279 item 4): hiçbir aggregate güncellenmedi, evaluator da
            // çalıştırılmamalı — çağıran (AnswerSubmittedConsumer) false döndüğünde bunu atlar.
            return false;
        }

        var (questionAggregate, previousPoints) = await UpdateStudentQuestionAggregateAsync(message, award.Delta, cancellationToken);
        var subjectAggregate = await UpdateStudentSubjectAggregateAsync(message, award.Delta, cancellationToken);
        var activity = await UpdateDailyActivityAsync(message, award.Delta, cancellationToken);

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
            // StudentDailyActivity/AnswerPointAward) unique index ihlalleri BURADA yakalanmaz, yukarı fırlar
            // (review fix, issue #243): aksi halde eşzamanlı ama GERÇEKTEN FARKLI bir cevabın puanı sessizce
            // kaybolurdu.
            return false;
        }

        return true;
    }

    private readonly record struct PointAwardResolution(bool IsStale, int Delta);

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

        var newPoints = message.IsCorrect ? cappedPoint : 0;
        var previousAwarded = award?.PointsAwarded ?? 0;
        var delta = newPoints - previousAwarded;

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

        return new PointAwardResolution(false, delta);
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
        AnswerSubmittedEvent message, int pointsDelta, CancellationToken cancellationToken)
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

        // issue #279 item 4: mutlak ekleme değil, (TestInstanceId, QuestionId) başına delta uygulanır.
        aggregate.TotalPoints = Math.Max(0, aggregate.TotalPoints + pointsDelta);

        aggregate.LastAnsweredAtUtc = message.SubmittedAt;
        return (aggregate, previousPoints);
    }

    private async Task<StudentSubjectAggregate?> UpdateStudentSubjectAggregateAsync(AnswerSubmittedEvent message, int pointsDelta, CancellationToken cancellationToken)
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
        }

        subjectAggregate.TotalPoints = Math.Max(0, subjectAggregate.TotalPoints + pointsDelta);

        return subjectAggregate;
    }

    private async Task<StudentDailyActivity> UpdateDailyActivityAsync(AnswerSubmittedEvent message, int pointsDelta, CancellationToken cancellationToken)
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
        }

        // issue #279 item 4: cevap değişikliğinin puanı, DELTA'nın ait olduğu event'in gününe (bugüne)
        // yansır — orijinal cevabın günü ayrıca izlenmiyor. Nadir bir uç durum (gece yarısını aşan cevap
        // değişikliği günü hafifçe kaydırabilir); kabul edilebilir bilinen sınırlama (bkz. rapor).
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
