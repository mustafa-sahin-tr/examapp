using System.Security.Claims;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using Hangfire.Dashboard;

namespace ExamApp.Api.Services.QuestionTransfer;

/// <summary>
/// Production Hangfire dashboard erişimi: Admin/SuperAdmin ya da hesabı ONAYLI öğretmen (issue #287, security review L3).
/// Yalnızca <c>IsInRole("Teacher")</c> yetmez — kayıtta Teacher rolü hemen verilir, hesap admin onayına kadar bekler.
/// Async filtre: onay kontrolü <see cref="IApprovedTeacherGuard"/> ile DB'den yapılır (istek scope'undan çözülür).
/// Profil çözülemezse fail-closed (erişim yok).
/// </summary>
public class HangfireDashboardAuthFilter : IDashboardAsyncAuthorizationFilter
{
    public async Task<bool> AuthorizeAsync(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var user = httpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        if (user.IsInRole("Admin") || user.IsInRole("SuperAdmin"))
            return true;

        if (!user.IsInRole("Teacher"))
            return false;

        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
            return false;

        try
        {
            var services = httpContext.RequestServices;
            var profile = await services.GetRequiredService<IUserProfileProvider>().GetAsync(sub, httpContext.RequestAborted);
            if (profile is not { Id: > 0 })
                return false;

            return await services.GetRequiredService<IApprovedTeacherGuard>().CheckAsync(profile.Id, httpContext.RequestAborted)
                   == TeacherApprovalCheck.Approved;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail-closed: profil/DB hatasında dashboard açılmaz.
            return false;
        }
    }
}
