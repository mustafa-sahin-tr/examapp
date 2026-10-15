using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Parents.Enforcement;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Services.Parents;

public static class ParentInvitationServiceCollectionExtensions
{
    /// <summary>
    /// Issue #440 / #441: veli daveti (auth-api <c>parent-accounts</c>, #438'in yeniden denemesiz named client'ı — <see cref="ParentChildAccountServiceCollectionExtensions.AddParentChildAccounts"/>
    /// SONRA çağrılmalı), öğrenci veli durumu (bellek önbellekli), Development dev-bağlantı deposu, hatırlatma job'ı, yaptırım (policy +
    /// sonuç işleyicisi — <c>AddApprovedTeacherAuthorization()</c>'dan SONRA) ve rate limit kovaları (<c>AddAdminUserListRateLimiting()</c>'ten
    /// SONRA). Ayarlar <c>ParentRequirement</c> bölümü (açılışta doğrulanır).
    /// </summary>
    public static IServiceCollection AddParentInvitations(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddOptions<ParentRequirementOptions>()
            .BindConfiguration(ParentRequirementOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IParentActivationDevLinkStore, ParentActivationDevLinkStore>();
        services.AddSingleton<IStudentParentStatusCache, StudentParentStatusCache>();
        services.AddScoped<IStudentParentStatusService, StudentParentStatusService>();
        services.AddScoped<IParentAccountDirectory, AuthApiParentAccountDirectory>();
        services.AddScoped<IParentInvitationService, ParentInvitationService>();
        services.AddScoped<IParentGraceReminderJob, ParentGraceReminderJob>();

        services.AddParentRequirementEnforcement();
        services.AddParentInvitationRateLimiting();
        return services;
    }
}
