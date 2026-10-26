using BadgeService.Entities;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Issue #572 (epic #569): <see cref="ActivityCompletedEvent"/> tüketicisi — etkinlik rozetleri (sayı / süre / seri).
/// Etkinlikler PUANSIZDIR: bu consumer puan, XP, <c>StudentDailyActivity</c>, <c>Student{Question,Subject}Aggregate</c>,
/// <c>StudentPointsOutbox</c> ve <c>AnswerPointAward</c>'a HİÇ dokunmaz; yalnız <see cref="StudentActivityCompletion"/>
/// ledger'ına yazar ve <see cref="BadgeEvaluator.EvaluateActivityCompletedAsync"/> ile etkinlik rozetlerini değerlendirir.
/// Kazanılan rozet bugünkü akıştan geçer (BadgeEarned + bildirim + StudentBadgeEarnedEvent outbox → veli paneli).
///
/// Idempotency (iki katman, ikisi de DB unique index'i): (1) <see cref="ActivityCompletedEvent.EventId"/> — aynı mesajın
/// tekrar teslimi; (2) (UserId, WorksheetInstanceId) — öğrenci + etkinlik + deneme; farklı EventId ile gelen aynı deneme
/// (ör. producer'ın tekrar yazması). Önce exists kontrolü, eşzamanlı yarışta unique ihlali (23505, yalnız bu iki index)
/// no-op sayılır. Aynı etkinliğin YENİ bir denemesi yeni satırdır (süre/seri sayılır) ama sayı rozeti
/// COUNT(DISTINCT ActivityId) olduğu için artmaz. Rozet değerlendirmesi duplicate teslimde de ÇALIŞIR (AnswerSubmitted ile
/// aynı gerekçe: ilk teslimde ledger commit olup değerlendirme düştüyse rozet kaybolmasın; evaluator idempotent).
///
/// Nitelik eşiği: sayı ve seri yalnız AnsweredItemCount >= max(1, ceil(ItemCount/2)) olan tamamlamaları sayar
/// (<see cref="StudentActivityCompletion.CountsTowardBadges"/>); süre her tamamlamayı sayar.
///
/// Reset guard (issue #396): <c>CompletedAtUtc</c> kullanıcının <see cref="UserResetMarker.ResetAtUtc"/>'ından önceyse
/// event yok sayılır (reset ledger'ı sildiği için aksi halde sıfırlanmış öğrenci eski denemeyle rozet kazanırdı).
///
/// Hata yolu: exception yutulmaz. <see cref="ActivityCompletedConsumerDefinition"/> 1s/5s/15s ile 3 retry uygular;
/// tükenirse mesaj <c>badge-service_error</c> kuyruğuna (dead-letter) düşer. Retry güvenlidir (yukarıdaki idempotency).
/// Geçersiz mesaj (UserId/ActivityId/WorksheetInstanceId &lt;= 0) retry ile düzelmez: uyarıyla atlanır. Log korelasyonu: EventId.
/// </summary>
public class ActivityCompletedConsumer : IConsumer<ActivityCompletedEvent>
{
    private readonly BadgeDbContext _db;
    private readonly BadgeEvaluator _evaluator;
    private readonly ILogger<ActivityCompletedConsumer> _logger;

    public ActivityCompletedConsumer(BadgeDbContext db, BadgeEvaluator evaluator, ILogger<ActivityCompletedConsumer> logger)
    {
        _db = db;
        _evaluator = evaluator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ActivityCompletedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (e.UserId <= 0 || e.ActivityId <= 0 || e.WorksheetInstanceId <= 0)
        {
            _logger.LogWarning(
                "[ActivityCompleted] Geçersiz mesaj atlandı (EventId={EventId}, UserId={UserId}, ActivityId={ActivityId}, InstanceId={InstanceId}).",
                e.EventId, e.UserId, e.ActivityId, e.WorksheetInstanceId);
            return;
        }

        var completedAt = EventVersion.Normalize(e.CompletedAtUtc);

        var resetAt = await _db.UserResetMarkers
            .AsNoTracking()
            .Where(x => x.UserId == e.UserId)
            .Select(x => (DateTime?)x.ResetAtUtc)
            .FirstOrDefaultAsync(ct);
        if (resetAt is not null && completedAt < EventVersion.Normalize(resetAt.Value))
        {
            _logger.LogInformation(
                "[ActivityCompleted] Sıfırlama öncesi tamamlanmış etkinlik yok sayıldı (EventId={EventId}, UserId={UserId}, CompletedAt={CompletedAt:o}, ResetAt={ResetAt:o}).",
                e.EventId, e.UserId, completedAt, resetAt.Value);
            return;
        }

        var duplicate = await _db.StudentActivityCompletions.AnyAsync(
            x => x.EventId == e.EventId || (x.UserId == e.UserId && x.WorksheetInstanceId == e.WorksheetInstanceId), ct);

        if (!duplicate)
        {
            _db.StudentActivityCompletions.Add(new StudentActivityCompletion
            {
                Id = Guid.NewGuid(),
                EventId = e.EventId,
                UserId = e.UserId,
                ActivityId = e.ActivityId,
                WorksheetInstanceId = e.WorksheetInstanceId,
                SubjectId = e.SubjectId,
                ActiveSeconds = ActivityCompletionStats.ClampSeconds(e.ActiveSeconds),
                ItemCount = Math.Max(0, e.ItemCount),
                AnsweredItemCount = Math.Max(0, e.AnsweredItemCount),
                CountsTowardBadges = ActivityCompletionStats.QualifiesForBadges(e.ItemCount, e.AnsweredItemCount),
                CompletedAtUtc = completedAt,
                ActivityDate = ActivityCompletionStats.ToIstanbulDate(completedAt),
            });

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsIdempotencyViolation(ex))
            {
                // Eşzamanlı aynı teslim kazandı; bu kopya no-op. Tracker'ı temizle ki evaluator başarısız insert'i görmesin.
                _db.ChangeTracker.Clear();
                duplicate = true;
            }
        }

        if (duplicate)
        {
            _logger.LogInformation(
                "[ActivityCompleted] Zaten işlenmiş (EventId={EventId}, UserId={UserId}, InstanceId={InstanceId}); yalnız rozet değerlendirmesi çalışıyor.",
                e.EventId, e.UserId, e.WorksheetInstanceId);
        }

        await _evaluator.EvaluateActivityCompletedAsync(
            e.UserId, e.ClientId, ActivityCompletionStats.ToIstanbulDate(completedAt), ct);
    }

    private static bool IsIdempotencyViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException
        {
            SqlState: "23505",
            ConstraintName: BadgeDbContext.StudentActivityCompletion_EventIdIndex
                or BadgeDbContext.StudentActivityCompletion_UserInstanceIndex
        };
}
