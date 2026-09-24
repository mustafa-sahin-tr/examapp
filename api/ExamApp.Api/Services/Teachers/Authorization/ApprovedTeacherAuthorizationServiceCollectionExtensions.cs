using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Services.Teachers.Authorization;

public static class ApprovedTeacherAuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// issue #287: onaysız öğretmen kapısı — <see cref="ApprovedTeacherPolicies"/> policy'leri, guard'ı kullanan handler
    /// ve TeacherNotApproved 403 gövdesini yazan result handler. <c>IApprovedTeacherGuard</c> ayrıca kayıtlı olmalı.
    /// </summary>
    public static IServiceCollection AddApprovedTeacherAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ApprovedTeacherPolicies.TeacherCapability, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ApprovedTeacherRequirement(ApprovedTeacherPolicies.AdminRoles)));

            options.AddPolicy(ApprovedTeacherPolicies.TeacherOrStudentCapability, policy => policy
                .RequireAuthenticatedUser()
                // security review L4: Student muafiyet DEĞİL (Teacher+Student çift rollü onaysız öğretmen kapıya takılır).
                .AddRequirements(new ApprovedTeacherRequirement(ApprovedTeacherPolicies.AdminRoles)));
        });

        // Scoped: scoped guard'a (istek başı önbellek) ve profil sağlayıcısına bağlı.
        services.AddScoped<IAuthorizationHandler, ApprovedTeacherAuthorizationHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler,
            ApprovedTeacherAuthorizationResultHandler>();
        return services;
    }
}
