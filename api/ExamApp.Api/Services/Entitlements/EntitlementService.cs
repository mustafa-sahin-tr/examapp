using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
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
            .Select(a => new ResolvedPlan(a.PlanId, a.Plan.Key, a.Plan.Name, a.ValidToUtc))
            .FirstOrDefaultAsync(ct);
        if (assigned is not null)
            return assigned;

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

    public async Task<AssignPlanError> AssignPlanAsync(int userId, string planKey, DateTime? validFromUtc, DateTime? validToUtc,
        int assignedByUserId, CancellationToken ct = default)
    {
        var plan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Key == planKey && p.IsActive, ct);
        if (plan is null)
            return AssignPlanError.UnknownPlan;

        var from = (validFromUtc ?? _clock.GetUtcNow().UtcDateTime).ToUniversalTime();
        var to = validToUtc?.ToUniversalTime();
        if (to is { } end && end <= from)
            return AssignPlanError.InvalidRange;

        // Hesap başına tek plan: yeni atamanın başında hâlâ açık olanları kapat, sonra başlayacak olanları iptal et.
        var overlapping = await _db.UserSubscriptionPlans
            .Where(a => a.UserId == userId && (a.ValidToUtc == null || a.ValidToUtc > from))
            .ToListAsync(ct);
        foreach (var existing in overlapping)
        {
            if (existing.ValidFromUtc >= from)
            {
                existing.IsDeleted = true;
            }
            else
            {
                existing.ValidToUtc = from;
            }
        }

        _db.UserSubscriptionPlans.Add(new UserSubscriptionPlan
        {
            UserId = userId,
            PlanId = plan.Id,
            ValidFromUtc = from,
            ValidToUtc = to,
        });
        _db.SetCurrentUser(assignedByUserId);
        await _db.SaveChangesAsync(ct);
        return AssignPlanError.None;
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
