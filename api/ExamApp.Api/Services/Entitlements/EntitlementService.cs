using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Issue #1019: planlar, özellik başına aylık sınır ve kullanım defteri (<see cref="EntitlementUsage"/>) üzerinden hak çözümler.
/// Plan tablolarında özelliğe özgü mantık yoktur; kullanımın ne zaman sayıldığına özellik karar verir ve
/// <see cref="RecordUsageAsync"/> çağırır.
/// </summary>
public sealed class EntitlementService : IEntitlementService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public EntitlementService(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ResolvedPlan> ResolvePlanAsync(int userId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        // Birden fazla geçerli atama olursa en son başlayan kazanır (atama normalde öncekini kapatır).
        var assigned = await _db.UserSubscriptionPlans.AsNoTracking()
            .Where(a => a.UserId == userId && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now) && a.Plan.IsActive)
            .OrderByDescending(a => a.ValidFromUtc).ThenByDescending(a => a.Id)
            .Select(a => new { Plan = new ResolvedPlan(a.PlanId, a.Plan.Key, a.Plan.Name, a.ValidToUtc, false, a.ScopeSubjectId), a.Plan.SortOrder })
            .FirstOrDefaultAsync(ct);
        // Issue #1092: okul lisansı kişisel atamadan üst kademede değilse lisans kazanır.
        var license = (await SchoolLicensePlans.ForUsersAsync(_db, [userId], now, ct)).GetValueOrDefault(userId);
        if (SchoolLicensePlans.Pick(assigned?.Plan, assigned?.SortOrder, license) is { } plan)
            return plan;

        return await _db.SubscriptionPlans.AsNoTracking()
            .Where(p => p.Key == SubscriptionPlan.FreeKey)
            .Select(p => new ResolvedPlan(p.Id, p.Key, p.Name))
            .FirstOrDefaultAsync(ct) ?? new ResolvedPlan(null, null, null);
    }

    public async Task<EntitlementCheck> CheckAsync(EntitlementSubject subject, string featureKey, CancellationToken ct = default)
    {
        var plan = await ResolvePlanAsync(subject.UserId, ct);
        var (monthStart, resetsAt) = CurrentMonth();
        var used = await _db.EntitlementUsages.AsNoTracking()
            .CountAsync(u => u.UserId == subject.UserId && u.FeatureKey == featureKey && u.UsedAtUtc >= monthStart && u.UsedAtUtc < resetsAt, ct);

        if (IsRoleUnlimited(subject, featureKey))
            return new EntitlementCheck(true, null, used, null, plan.Key);

        // Satır yoksa izin yok (0): yeni özellik, sınırı tanımlanana kadar kapalı kalır.
        var limit = plan.PlanId is { } planId
            ? await _db.SubscriptionPlanFeatureLimits.AsNoTracking()
                .Where(l => l.PlanId == planId && l.FeatureKey == featureKey)
                .Select(l => new { l.MonthlyLimit })
                .FirstOrDefaultAsync(ct)
            : null;
        var monthlyLimit = limit is null ? 0 : limit.MonthlyLimit;

        return monthlyLimit is null
            ? new EntitlementCheck(true, null, used, null, plan.Key)
            : new EntitlementCheck(used < monthlyLimit, monthlyLimit, used, resetsAt, plan.Key);
    }

    public async Task<bool> RecordUsageAsync(int userId, string featureKey, string referenceKey, CancellationToken ct = default)
    {
        if (await _db.EntitlementUsages.AnyAsync(u => u.FeatureKey == featureKey && u.ReferenceKey == referenceKey, ct))
            return false;

        _db.EntitlementUsages.Add(new EntitlementUsage
        {
            UserId = userId,
            FeatureKey = featureKey,
            ReferenceKey = referenceKey,
            UsedAtUtc = _clock.GetUtcNow().UtcDateTime,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Eşzamanlı ikinci kayıt tekil indekse takıldıysa kullanım zaten sayılmış; başka bir hata yeniden fırlatılır.
            _db.ChangeTracker.Clear();
            if (await _db.EntitlementUsages.AsNoTracking().AnyAsync(u => u.FeatureKey == featureKey && u.ReferenceKey == referenceKey, ct))
                return false;
            throw;
        }
    }

    public Task<AssignPlanError> AssignPlanAsync(int userId, string planKey, DateTime? validFromUtc, DateTime? validToUtc,
        int assignedByUserId, CancellationToken ct = default) =>
        AssignPlanToUsersAsync([userId], planKey, validFromUtc, validToUtc, assignedByUserId, schoolId: null, ct);

    public Task<AssignPlanError> AssignPlanToUsersAsync(IReadOnlyCollection<int> userIds, string planKey, DateTime? validFromUtc,
        DateTime? validToUtc, int assignedByUserId, int? schoolId, CancellationToken ct = default) =>
        AssignPlanToUsersAsync(userIds, planKey, validFromUtc, validToUtc, assignedByUserId, schoolId, terms: null, ct);

    /// <summary><paramref name="terms"/>: #1108 satın almanın kapsamı, süresi ve ödenen fiyatı (yalnız sahte ödemede).</summary>
    private async Task<AssignPlanError> AssignPlanToUsersAsync(IReadOnlyCollection<int> userIds, string planKey, DateTime? validFromUtc,
        DateTime? validToUtc, int assignedByUserId, int? schoolId, PurchaseTerms? terms, CancellationToken ct)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Key == planKey && p.IsActive, ct);
        if (plan is null)
            return AssignPlanError.UnknownPlan;

        var now = _clock.GetUtcNow().UtcDateTime;
        var from = (validFromUtc ?? now).ToUniversalTime();
        var to = validToUtc?.ToUniversalTime();
        if (to is { } end && end <= from)
            return AssignPlanError.InvalidRange;

        var ids = userIds.Distinct().ToList();
        // Hesap başına tek plan: yeni atamanın başında hâlâ açık olanları kapat, sonra başlayacak olanları iptal et.
        var overlapping = await _db.UserSubscriptionPlans.Include(a => a.Plan)
            .Where(a => ids.Contains(a.UserId) && (a.ValidToUtc == null || a.ValidToUtc > from))
            .ToListAsync(ct);
        var byUser = overlapping.ToLookup(a => a.UserId);
        foreach (var userId in ids)
        {
            string? fromPlanKey = null;
            foreach (var existing in byUser[userId])
            {
                if (existing.ValidFromUtc >= from)
                {
                    existing.IsDeleted = true;
                }
                else
                {
                    existing.ValidToUtc = from;
                    fromPlanKey = existing.Plan.Key;
                }
            }

            _db.UserSubscriptionPlans.Add(new UserSubscriptionPlan
            {
                UserId = userId,
                PlanId = plan.Id,
                ValidFromUtc = from,
                ValidToUtc = to,
                ScopeSubjectId = terms?.SubjectId,
                Duration = terms?.Duration ?? PlanDuration.Monthly,
                PricePaid = terms?.Price,
                DiscountPercent = terms?.DiscountPercent ?? 0,
            });
            _db.SubscriptionPlanAssignmentAudits.Add(new SubscriptionPlanAssignmentAudit
            {
                UserId = userId,
                ActorUserId = assignedByUserId,
                Action = SubscriptionPlanAuditAction.Assigned,
                FromPlanKey = fromPlanKey,
                ToPlanKey = plan.Key,
                ValidFromUtc = from,
                ValidToUtc = to,
                SchoolId = schoolId,
                OccurredAtUtc = now,
            });
        }

        _db.SetCurrentUser(assignedByUserId);
        await _db.SaveChangesAsync(ct);
        return AssignPlanError.None;
    }

    public async Task<bool> EndPlanAsync(int userId, int endedByUserId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var open = await _db.UserSubscriptionPlans.Include(a => a.Plan)
            .Where(a => a.UserId == userId && (a.ValidToUtc == null || a.ValidToUtc > now))
            .ToListAsync(ct);
        if (open.Count == 0)
            return false;

        string? fromPlanKey = null;
        foreach (var assignment in open)
        {
            // Başlamış olan bugün biter; ileride başlayacak olan iptal edilir. Kullanıcı Free'ye döner.
            if (assignment.ValidFromUtc > now)
            {
                assignment.IsDeleted = true;
            }
            else
            {
                assignment.ValidToUtc = now;
                fromPlanKey = assignment.Plan.Key;
            }
        }

        _db.SubscriptionPlanAssignmentAudits.Add(new SubscriptionPlanAssignmentAudit
        {
            UserId = userId,
            ActorUserId = endedByUserId,
            Action = SubscriptionPlanAuditAction.Ended,
            FromPlanKey = fromPlanKey,
            ToPlanKey = null,
            ValidToUtc = now,
            OccurredAtUtc = now,
        });
        _db.SetCurrentUser(endedByUserId);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public Task<bool> HasUsedTrialAsync(int userId, CancellationToken ct = default) =>
        _db.UserSubscriptionPlans.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(a => a.UserId == userId && a.Plan.TrialDays != null, ct);

    public async Task<SelfServicePlanError> StartTrialAsync(int userId, IReadOnlySet<SubscriptionPlanAudience> audiences,
        CancellationToken ct = default)
    {
        var trial = await _db.SubscriptionPlans.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Key == SubscriptionPlan.TrialKey && p.IsActive && p.TrialDays != null, ct);
        if (trial is null)
            return SelfServicePlanError.UnknownPlan;
        if (!audiences.Contains(trial.Audience))
            return SelfServicePlanError.NotAvailable;

        // #1090: plan kilidi denemeyi ilk istekte başlatır; ilk sayfanın eş zamanlı istekleri kullanıcı kilidinde sıralanır.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await _db.Database.AcquireTrialStartLockAsync(userId, ct);
            if (await HasUsedTrialAsync(userId, ct))
                return SelfServicePlanError.TrialAlreadyUsed;

            var now = _clock.GetUtcNow().UtcDateTime;
            await AssignPlanAsync(userId, trial.Key, now, now.AddDays(trial.TrialDays!.Value), assignedByUserId: userId, ct);
            await tx.CommitAsync(ct);
            return SelfServicePlanError.None;
        });
    }

    public async Task<SelfServicePlanError> CheckoutAsync(int userId, string planKey, IReadOnlySet<SubscriptionPlanAudience> audiences,
        int? purchasedByUserId = null, PlanPurchase? purchase = null, CancellationToken ct = default)
    {
        var plan = await _db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Key == planKey && p.IsActive, ct);
        if (plan is null)
            return SelfServicePlanError.UnknownPlan;
        if (!audiences.Contains(plan.Audience) || plan.MonthlyPrice <= 0 || plan.TrialDays is not null)
            return SelfServicePlanError.NotAvailable;

        var now = _clock.GetUtcNow().UtcDateTime;
        purchase ??= new PlanPurchase();
        var terms = await PriceAsync(userId, plan, purchase, purchasedByUserId ?? userId, now, ct);
        if (terms is null)
            return SelfServicePlanError.InvalidOption;

        await AssignPlanToUsersAsync([userId], plan.Key, now, StudentPlanPricing.ValidTo(now, purchase.Duration),
            purchasedByUserId ?? userId, schoolId: null, terms, ct);
        return SelfServicePlanError.None;
    }

    public async Task<PlanPurchaseStatus?> PurchaseStatusAsync(int userId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var purchase = await CurrentPurchase(userId, now).AsNoTracking()
            .Select(a => new { a.Duration, a.PricePaid, a.Plan.Currency, a.ValidFromUtc, a.ValidToUtc, a.CancelledAtUtc })
            .FirstOrDefaultAsync(ct);
        return purchase is null ? null : new PlanPurchaseStatus(purchase.Duration, purchase.PricePaid!.Value, purchase.Currency,
            purchase.ValidToUtc, purchase.CancelledAtUtc, PurchaseTermsPolicy.FullRefundUntil(purchase.Duration, purchase.ValidFromUtc));
    }

    public async Task<PlanCancellationResult> CancelPlanAsync(int userId, int cancelledByUserId, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var purchase = await CurrentPurchase(userId, now).Include(a => a.Plan).FirstOrDefaultAsync(ct);
        if (purchase is null)
            return new PlanCancellationResult(CancelPlanError.NothingToCancel);
        if (purchase.CancelledAtUtc is not null)
            return new PlanCancellationResult(CancelPlanError.AlreadyCancelled, purchase.ValidToUtc);

        purchase.CancelledAtUtc = now;
        purchase.CancelledByUserId = cancelledByUserId;
        // Cayma süresinde: plan şimdi biter, ödenen tutarın tamamı iade kaydına.
        if (PurchaseTermsPolicy.FullRefundUntil(purchase.Duration, purchase.ValidFromUtc) is { } refundUntil && now < refundUntil)
        {
            purchase.ValidToUtc = now;
            purchase.RefundAmount = purchase.PricePaid;
        }

        _db.SubscriptionPlanAssignmentAudits.Add(new SubscriptionPlanAssignmentAudit
        {
            UserId = userId,
            ActorUserId = cancelledByUserId,
            Action = SubscriptionPlanAuditAction.Cancelled,
            FromPlanKey = purchase.Plan.Key,
            ToPlanKey = purchase.RefundAmount is null ? purchase.Plan.Key : null,
            ValidFromUtc = purchase.ValidFromUtc,
            ValidToUtc = purchase.ValidToUtc,
            OccurredAtUtc = now,
        });
        _db.SetCurrentUser(cancelledByUserId);
        await _db.SaveChangesAsync(ct);
        return new PlanCancellationResult(CancelPlanError.None, purchase.ValidToUtc, purchase.RefundAmount, purchase.Plan.Currency);
    }

    // Süren satın alma: ödenmiş (PricePaid) ve şu an geçerli atama; çözümleme gibi en son başlayan.
    private IQueryable<UserSubscriptionPlan> CurrentPurchase(int userId, DateTime now) =>
        _db.UserSubscriptionPlans
            .Where(a => a.UserId == userId && a.PricePaid != null && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now))
            .OrderByDescending(a => a.ValidFromUtc).ThenByDescending(a => a.Id);

    /// <summary>
    /// Issue #1108: seçeneğin fiyatı; satılmayan seçenekte null. Sınıfı olan öğrencinin planı fiyat tablosundan (band, sınıf, kapsam,
    /// süre); öğretmen planı ve sınıfı olmayan öğrenci yalnız aylık + bütün dersler, kartın aylık fiyatıyla.
    /// </summary>
    private async Task<PurchaseTerms?> PriceAsync(int userId, SubscriptionPlan plan, PlanPurchase purchase, int payerUserId, DateTime now,
        CancellationToken ct)
    {
        if (purchase.Duration == PlanDuration.Summer && !StudentPlanPricing.SummerOnSale(now))
            return null;

        var (gradeId, gradeLevel) = plan.Audience == SubscriptionPlanAudience.Student
            ? await StudentPlanPricing.GradeOfAsync(_db, userId, ct)
            : (null, null);
        decimal price;
        if (StudentPlanPricing.BandOf(gradeLevel) is { } band)
        {
            if (purchase.SubjectId is { } subjectId
                && !await StudentPlanPricing.SingleSubjectsOf(_db, gradeId).AnyAsync(gs => gs.SubjectId == subjectId, ct))
                return null;
            var scope = purchase.SubjectId is null ? PlanScope.AllSubjects : PlanScope.SingleSubject;
            var row = await _db.SubscriptionPlanPrices.AsNoTracking()
                .Where(p => p.PlanId == plan.Id && p.Band == band && (p.GradeLevel == null || p.GradeLevel == gradeLevel)
                    && p.Scope == scope && p.Duration == purchase.Duration)
                .OrderByDescending(p => p.GradeLevel != null)
                .Select(p => (decimal?)p.Price)
                .FirstOrDefaultAsync(ct);
            if (row is null)
                return null;
            price = row.Value;
        }
        else
        {
            if (purchase.SubjectId is not null || purchase.Duration != PlanDuration.Monthly)
                return null;
            price = plan.MonthlyPrice;
        }

        var discount = plan.Audience == SubscriptionPlanAudience.Student && payerUserId != userId
            ? await StudentPlanPricing.SiblingDiscountAsync(_db, payerUserId, userId, now, ct)
            : 0;
        return new PurchaseTerms(purchase.SubjectId, purchase.Duration, StudentPlanPricing.Discounted(price, discount), discount);
    }

    private static bool IsRoleUnlimited(EntitlementSubject subject, string featureKey) =>
        subject.IsAdmin
        || (EntitlementRoles.UnlimitedByFeature.TryGetValue(featureKey, out var roles) && roles.Any(subject.Roles.Contains));

    private (DateTime MonthStart, DateTime ResetsAt) CurrentMonth()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (start, start.AddMonths(1));
    }
}
