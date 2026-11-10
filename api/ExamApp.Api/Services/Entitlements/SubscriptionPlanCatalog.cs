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

    /// <param name="includePrices">#1108: öğrenci planlarının fiyat tablosu (admin düzenleyicisi için).</param>
    public static async Task<List<SubscriptionPlanDto>> ListAsync(AppDbContext db, Expression<Func<SubscriptionPlan, bool>> filter,
        CancellationToken ct, bool includePrices = false)
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

        if (includePrices)
        {
            var keys = plans.Select(p => p.Plan.Key).ToList();
            var prices = (await db.SubscriptionPlanPrices.AsNoTracking()
                    .Where(p => keys.Contains(p.Plan.Key))
                    .Select(p => new { p.Plan.Key, p.Band, p.GradeLevel, p.Scope, p.Duration, p.Price })
                    .ToListAsync(ct))
                .OrderBy(p => p.Band).ThenBy(p => p.GradeLevel).ThenBy(p => p.Scope).ThenBy(p => p.Duration)
                .ToLookup(p => p.Key);
            foreach (var p in plans.Where(p => p.Audience == SubscriptionPlanAudience.Student))
            {
                p.Plan.Prices = prices[p.Plan.Key].Select(r => new SubscriptionPlanPriceRowDto
                {
                    Band = r.Band.ToString(), GradeLevel = r.GradeLevel, Scope = r.Scope.ToString(), Duration = r.Duration.ToString(), Price = r.Price,
                }).ToList();
            }
        }
        return plans.Select(p => p.Plan).ToList();
    }
}
