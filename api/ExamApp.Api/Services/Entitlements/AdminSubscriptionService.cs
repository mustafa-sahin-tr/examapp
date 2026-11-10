using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>
/// Issue #1030: admin plan ataması ekranı — kullanıcı araması, kullanıcının planı ve geçmişi, okula toplu atama. Ad / e-posta
/// auth-api'dedir (arama <c>users/search</c>, ad çözümü <c>users/lookup</c>); atama ve audit <see cref="IEntitlementService"/>'te.
/// </summary>
public interface IAdminSubscriptionService
{
    Task<List<AdminSubscriptionUserDto>> SearchAsync(string query, string? role, CancellationToken ct = default);

    /// <summary>Kullanıcıların bugünkü planı; ataması olmayan kullanıcı Free'dir (<see cref="IEntitlementService.ResolvePlanAsync"/> ile aynı).</summary>
    Task<Dictionary<int, ResolvedPlan>> CurrentPlansAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);

    /// <summary><see langword="null"/> = auth-api'de böyle kullanıcı yok.</summary>
    Task<AdminSubscriptionUserDetailDto?> GetDetailAsync(int userId, CancellationToken ct = default);

    /// <summary><see langword="null"/> = okul yok.</summary>
    Task<SchoolSubscriptionMembersDto?> GetSchoolMembersAsync(int schoolId, CancellationToken ct = default);

    Task<(AdminSchoolAssignError Error, int Assigned)> AssignSchoolAsync(
        int schoolId, AssignSchoolSubscriptionPlanDto request, int adminUserId, CancellationToken ct = default);

    /// <summary>
    /// Issue #1086: planın adını, fiyatını, deneme süresini, etkinliğini ve bütün sınırlarını değiştirir; her kayıt
    /// <c>SubscriptionPlanChangeAudits</c>'e yazılır. Sınırlar her denetimde veritabanından okunduğu için sonraki denetimde geçerlidir.
    /// </summary>
    Task<AdminPlanUpdateError> UpdatePlanAsync(string planKey, UpdateSubscriptionPlanDto request, int adminUserId, CancellationToken ct = default);
}

public enum AdminPlanUpdateError
{
    None,
    PlanNotFound,
    InvalidName,
    InvalidPrice,
    InvalidTrialDays,
    UnknownFeature,
    AudienceMismatch,
    InvalidLimit,
    PlanRequired,
}

public enum AdminSchoolAssignError
{
    None,
    SchoolNotFound,
    UnknownPlan,
    InvalidRange,
    AudienceMismatch,
    NoMembers,
    TooManyMembers,
}

public sealed class AdminSubscriptionService(AppDbContext db, IEntitlementService entitlements, IAuthApiClient authApi, TimeProvider clock)
    : IAdminSubscriptionService
{
    /// <summary>Tek istekte atanabilecek üst sınır; daha büyük okul için kitle ayrı ayrı atanır.</summary>
    public const int MaxSchoolMembers = 5000;

    private const int HistoryLimit = 50;

    public async Task<List<AdminSubscriptionUserDto>> SearchAsync(string query, string? role, CancellationToken ct = default)
    {
        var users = await authApi.SearchUsersAsync(query, role, ct);
        var plans = await CurrentPlansAsync(users.Select(u => u.Id).ToList(), ct);

        return users.Select(u =>
        {
            plans.TryGetValue(u.Id, out var plan);
            return new AdminSubscriptionUserDto
            {
                UserId = u.Id,
                FullName = u.FullName,
                Email = EmailMask.Apply(u.Email),
                Role = u.Role,
                PlanKey = plan?.Key,
                PlanName = plan?.Name,
                ValidToUtc = plan?.ValidToUtc,
            };
        }).ToList();
    }

    public async Task<Dictionary<int, ResolvedPlan>> CurrentPlansAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var current = (await db.UserSubscriptionPlans.AsNoTracking()
                .Where(a => userIds.Contains(a.UserId) && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now) && a.Plan.IsActive)
                .Select(a => new { a.UserId, a.ValidFromUtc, a.Id, a.PlanId, a.Plan.Key, a.Plan.Name, a.ValidToUtc, a.Plan.SortOrder })
                .ToListAsync(ct))
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.ValidFromUtc).ThenByDescending(a => a.Id).First());

        var free = await db.SubscriptionPlans.AsNoTracking()
            .Where(p => p.Key == SubscriptionPlan.FreeKey)
            .Select(p => new { p.Id, p.Key, p.Name })
            .FirstOrDefaultAsync(ct);

        // Issue #1092: ResolvePlanAsync ile aynı sıra (lisans kişisel atamadan üst kademede değilse lisans).
        var licenses = await SchoolLicensePlans.ForUsersAsync(db, userIds, now, ct);

        return userIds.Distinct().ToDictionary(id => id, id =>
        {
            var personal = current.TryGetValue(id, out var plan) ? plan : null;
            return SchoolLicensePlans.Pick(
                    personal is null ? null : new ResolvedPlan(personal.PlanId, personal.Key, personal.Name, personal.ValidToUtc),
                    personal?.SortOrder, licenses.GetValueOrDefault(id))
                ?? new ResolvedPlan(free?.Id, free?.Key, free?.Name);
        });
    }

    public async Task<AdminSubscriptionUserDetailDto?> GetDetailAsync(int userId, CancellationToken ct = default)
    {
        var user = (await authApi.GetUsersByIdsOrThrowAsync([userId], ct)).FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return null;

        var plan = await entitlements.ResolvePlanAsync(userId, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var assignments = await db.UserSubscriptionPlans.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.ValidFromUtc).ThenByDescending(a => a.Id)
            .Take(HistoryLimit)
            .Select(a => new { a.Id, a.Plan.Key, a.Plan.Name, a.ValidFromUtc, a.ValidToUtc })
            .ToListAsync(ct);
        var changes = await db.SubscriptionPlanAssignmentAudits.AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Take(HistoryLimit)
            .ToListAsync(ct);

        var actorIds = changes.Select(c => c.ActorUserId).Where(id => id > 0 && id != userId).Distinct().ToList();
        var actors = actorIds.Count == 0
            ? new Dictionary<int, string>()
            : (await authApi.GetUsersByIdsAsync(actorIds, ct)).ToDictionary(u => u.Id, u => u.FullName);

        return new AdminSubscriptionUserDetailDto
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = EmailMask.Apply(user.Email),
            Role = user.Role,
            PlanKey = plan.Key,
            PlanName = plan.Name,
            ValidToUtc = plan.ValidToUtc,
            Assignments = assignments.Select(a => new AdminSubscriptionAssignmentDto
            {
                Id = a.Id,
                PlanKey = a.Key,
                PlanName = a.Name,
                ValidFromUtc = a.ValidFromUtc,
                ValidToUtc = a.ValidToUtc,
                Status = a.ValidFromUtc > now ? "Scheduled" : a.ValidToUtc is { } end && end <= now ? "Ended" : "Active",
            }).ToList(),
            Changes = changes.Select(c => new AdminSubscriptionChangeDto
            {
                OccurredAtUtc = c.OccurredAtUtc,
                Action = c.Action.ToString(),
                FromPlanKey = c.FromPlanKey,
                ToPlanKey = c.ToPlanKey,
                ValidFromUtc = c.ValidFromUtc,
                ValidToUtc = c.ValidToUtc,
                BySelf = c.ActorUserId == userId,
                ActorName = c.ActorUserId == userId ? user.FullName : actors.GetValueOrDefault(c.ActorUserId, string.Empty),
                SchoolId = c.SchoolId,
            }).ToList(),
        };
    }

    public async Task<SchoolSubscriptionMembersDto?> GetSchoolMembersAsync(int schoolId, CancellationToken ct = default)
    {
        if (!await db.Schools.AnyAsync(s => s.Id == schoolId, ct))
            return null;

        return new SchoolSubscriptionMembersDto
        {
            SchoolId = schoolId,
            Teachers = await TeacherUserIds(schoolId).CountAsync(ct),
            Students = await StudentUserIds(schoolId).CountAsync(ct),
        };
    }

    public async Task<(AdminSchoolAssignError Error, int Assigned)> AssignSchoolAsync(
        int schoolId, AssignSchoolSubscriptionPlanDto request, int adminUserId, CancellationToken ct = default)
    {
        if (!await db.Schools.AnyAsync(s => s.Id == schoolId, ct))
            return (AdminSchoolAssignError.SchoolNotFound, 0);

        var plan = await db.SubscriptionPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Key == request.PlanKey && p.IsActive, ct);
        if (plan is null)
            return (AdminSchoolAssignError.UnknownPlan, 0);
        // Öğretmen planı öğrencilere (ya da tersi) toplu atanmaz; tekil atamada admin serbesttir (deneme amaçlı).
        var mismatch = plan.Audience switch
        {
            SubscriptionPlanAudience.Teacher => request.Audience != SchoolPlanAudience.Teachers,
            SubscriptionPlanAudience.Student => request.Audience != SchoolPlanAudience.Students,
            _ => false,
        };
        if (mismatch)
            return (AdminSchoolAssignError.AudienceMismatch, 0);

        var userIds = new List<int>();
        if (request.Audience is SchoolPlanAudience.Teachers or SchoolPlanAudience.All)
            userIds.AddRange(await TeacherUserIds(schoolId).ToListAsync(ct));
        if (request.Audience is SchoolPlanAudience.Students or SchoolPlanAudience.All)
            userIds.AddRange(await StudentUserIds(schoolId).ToListAsync(ct));
        userIds = userIds.Distinct().ToList();
        if (userIds.Count == 0)
            return (AdminSchoolAssignError.NoMembers, 0);
        if (userIds.Count > MaxSchoolMembers)
            return (AdminSchoolAssignError.TooManyMembers, 0);

        var error = await entitlements.AssignPlanToUsersAsync(
            userIds, plan.Key, request.ValidFromUtc, request.ValidToUtc, adminUserId, schoolId, ct);
        return error switch
        {
            AssignPlanError.None => (AdminSchoolAssignError.None, userIds.Count),
            AssignPlanError.UnknownPlan => (AdminSchoolAssignError.UnknownPlan, 0),
            _ => (AdminSchoolAssignError.InvalidRange, 0),
        };
    }

    public async Task<AdminPlanUpdateError> UpdatePlanAsync(
        string planKey, UpdateSubscriptionPlanDto request, int adminUserId, CancellationToken ct = default)
    {
        var plan = await db.SubscriptionPlans.Include(p => p.FeatureLimits).FirstOrDefaultAsync(p => p.Key == planKey, ct);
        if (plan is null)
            return AdminPlanUpdateError.PlanNotFound;

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > SubscriptionPlan.NameMaxLength)
            return AdminPlanUpdateError.InvalidName;
        if (request.MonthlyPrice is < 0 or > MaxMonthlyPrice || decimal.Round(request.MonthlyPrice, 2) != request.MonthlyPrice)
            return AdminPlanUpdateError.InvalidPrice;
        // Süre yalnız deneme planındadır (atamada bitiş tarihi buradan hesaplanır, #1028).
        var isTrial = plan.Key == SubscriptionPlan.TrialKey;
        if (isTrial ? request.TrialDays is not (>= 1 and <= MaxTrialDays) : request.TrialDays is not null)
            return AdminPlanUpdateError.InvalidTrialDays;
        // Ataması olmayan kullanıcı Free'ye, kayıtta deneme planına çözülür: ikisi kapatılamaz.
        if (!request.IsActive && plan.Key is SubscriptionPlan.FreeKey or SubscriptionPlan.TrialKey)
            return AdminPlanUpdateError.PlanRequired;

        var limits = new Dictionary<string, int?>(StringComparer.Ordinal);
        foreach (var (featureKey, limit) in request.MonthlyLimits ?? [])
        {
            if (EntitlementFeatures.Find(featureKey) is not { } feature)
                return AdminPlanUpdateError.UnknownFeature;
            var audienceMatches = feature.Audience == EntitlementAudience.Both || plan.Audience switch
            {
                SubscriptionPlanAudience.Teacher => feature.Audience == EntitlementAudience.Teacher,
                SubscriptionPlanAudience.Student => feature.Audience == EntitlementAudience.Student,
                _ => true,
            };
            if (!audienceMatches)
                return AdminPlanUpdateError.AudienceMismatch;
            if (limit is < 0 or > MaxMonthlyLimit)
                return AdminPlanUpdateError.InvalidLimit;
            // Açık / kapalı özellikte sayı anlamsız: listedeyse dahildir.
            limits[featureKey] = feature.Kind == EntitlementFeatureKind.Toggle ? null : limit;
        }

        var before = Snapshot(plan);
        plan.Name = name;
        plan.MonthlyPrice = request.MonthlyPrice;
        plan.TrialDays = request.TrialDays;
        plan.IsActive = request.IsActive;
        // Listede olmayan özellik planda yoktur: satırı silinmez, 0'a çekilir (satır yok = 0; tekil indeks yeniden eklemeyi engellemez).
        foreach (var row in plan.FeatureLimits)
        {
            if (limits.Remove(row.FeatureKey, out var limit))
                row.MonthlyLimit = limit;
            else
                row.MonthlyLimit = 0;
        }
        foreach (var (featureKey, limit) in limits)
            plan.FeatureLimits.Add(new SubscriptionPlanFeatureLimit { FeatureKey = featureKey, MonthlyLimit = limit });

        db.SubscriptionPlanChangeAudits.Add(new SubscriptionPlanChangeAudit
        {
            PlanId = plan.Id,
            PlanKey = plan.Key,
            ActorUserId = adminUserId,
            BeforeJson = before,
            AfterJson = Snapshot(plan),
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
        return AdminPlanUpdateError.None;
    }

    public const decimal MaxMonthlyPrice = 1_000_000m;
    public const int MaxTrialDays = 365;
    public const int MaxMonthlyLimit = 1_000_000;

    // Kayıtta 0 sınırlı satırlar yazılmaz: "planda yok" ile aynıdır.
    private static string Snapshot(SubscriptionPlan plan) => JsonSerializer.Serialize(new
    {
        plan.Name,
        plan.MonthlyPrice,
        plan.TrialDays,
        plan.IsActive,
        MonthlyLimits = plan.FeatureLimits.Where(l => l.MonthlyLimit != 0).OrderBy(l => l.FeatureKey, StringComparer.Ordinal)
            .ToDictionary(l => l.FeatureKey, l => l.MonthlyLimit),
    });

    // Öğretmen: okula bağlı (Teacher.SchoolId; başvurusu bekleyen RequestedSchoolId sayılmaz). Öğrenci: yalnız doğrulanmış üyelik.
    private IQueryable<int> TeacherUserIds(int schoolId) =>
        db.Teachers.AsNoTracking().Where(t => t.SchoolId == schoolId).Select(t => t.UserId);

    private IQueryable<int> StudentUserIds(int schoolId) =>
        db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.SchoolVerifiedAt != null).Select(s => s.UserId);
}
