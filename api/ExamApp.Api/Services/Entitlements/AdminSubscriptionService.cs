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

    /// <summary><see langword="null"/> = auth-api'de böyle kullanıcı yok.</summary>
    Task<AdminSubscriptionUserDetailDto?> GetDetailAsync(int userId, CancellationToken ct = default);

    /// <summary><see langword="null"/> = okul yok.</summary>
    Task<SchoolSubscriptionMembersDto?> GetSchoolMembersAsync(int schoolId, CancellationToken ct = default);

    Task<(AdminSchoolAssignError Error, int Assigned)> AssignSchoolAsync(
        int schoolId, AssignSchoolSubscriptionPlanDto request, int adminUserId, CancellationToken ct = default);
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
        var ids = users.Select(u => u.Id).ToList();
        var now = clock.GetUtcNow().UtcDateTime;
        var current = (await db.UserSubscriptionPlans.AsNoTracking()
                .Where(a => ids.Contains(a.UserId) && a.ValidFromUtc <= now && (a.ValidToUtc == null || a.ValidToUtc > now) && a.Plan.IsActive)
                .Select(a => new { a.UserId, a.ValidFromUtc, a.Id, a.Plan.Key, a.Plan.Name, a.ValidToUtc })
                .ToListAsync(ct))
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.ValidFromUtc).ThenByDescending(a => a.Id).First());

        var free = await db.SubscriptionPlans.AsNoTracking()
            .Where(p => p.Key == SubscriptionPlan.FreeKey)
            .Select(p => new { p.Key, p.Name })
            .FirstOrDefaultAsync(ct);

        return users.Select(u =>
        {
            current.TryGetValue(u.Id, out var plan);
            return new AdminSubscriptionUserDto
            {
                UserId = u.Id,
                FullName = u.FullName,
                Email = EmailMask.Apply(u.Email),
                Role = u.Role,
                // Ataması olmayan kullanıcı Free'dir (ResolvePlanAsync ile aynı).
                PlanKey = plan?.Key ?? free?.Key,
                PlanName = plan?.Name ?? free?.Name,
                ValidToUtc = plan?.ValidToUtc,
            };
        }).ToList();
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

    // Öğretmen: okula bağlı (Teacher.SchoolId; başvurusu bekleyen RequestedSchoolId sayılmaz). Öğrenci: yalnız doğrulanmış üyelik.
    private IQueryable<int> TeacherUserIds(int schoolId) =>
        db.Teachers.AsNoTracking().Where(t => t.SchoolId == schoolId).Select(t => t.UserId);

    private IQueryable<int> StudentUserIds(int schoolId) =>
        db.Students.AsNoTracking().Where(s => s.SchoolId == schoolId && s.SchoolVerifiedAt != null).Select(s => s.UserId);
}
