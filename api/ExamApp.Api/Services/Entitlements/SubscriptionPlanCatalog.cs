using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Entitlements;

/// <summary>Issue #1027: plan kataloğunun okunması — admin tüm planları, kullanıcı rolüne uygun aktif planları görür.</summary>
public static class SubscriptionPlanCatalog
{
    /// <summary>Çağıranın görebileceği hedef kitleler: herkes <c>Any</c>; öğretmen / öğrenci kendi planlarını; Admin hepsini.</summary>
    public static IReadOnlySet<SubscriptionPlanAudience> AudiencesFor(ClaimsPrincipal principal)
    {
        var audiences = new HashSet<SubscriptionPlanAudience> { SubscriptionPlanAudience.Any };
        var isAdmin = principal.IsInRole(EntitlementRoles.Admin);
        if (isAdmin || principal.IsInRole("Teacher"))
            audiences.Add(SubscriptionPlanAudience.Teacher);
        if (isAdmin || principal.IsInRole("Student"))
            audiences.Add(SubscriptionPlanAudience.Student);
        return audiences;
    }

    public static async Task<List<SubscriptionPlanDto>> ListAsync(AppDbContext db, Expression<Func<SubscriptionPlan, bool>> filter,
        CancellationToken ct)
    {
        var plans = await db.SubscriptionPlans.AsNoTracking()
            .Where(filter)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .Select(p => new
            {
                Plan = new SubscriptionPlanDto
                {
                    Key = p.Key,
                    Name = p.Name,
                    DisplayNameKey = p.DisplayNameKey,
                    MonthlyPrice = p.MonthlyPrice,
                    Currency = p.Currency,
                    TrialDays = p.TrialDays,
                    IsActive = p.IsActive,
                },
                p.Audience,
                Limits = p.FeatureLimits.OrderBy(l => l.Id).Select(l => new { l.FeatureKey, l.MonthlyLimit }).ToList(),
            })
            .ToListAsync(ct);

        foreach (var p in plans)
        {
            p.Plan.Audience = p.Audience.ToString();
            p.Plan.MonthlyLimits = p.Limits.ToDictionary(l => l.FeatureKey, l => l.MonthlyLimit);
        }
        return plans.Select(p => p.Plan).ToList();
    }
}
