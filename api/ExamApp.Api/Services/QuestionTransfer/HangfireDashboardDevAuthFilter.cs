using Hangfire.Dashboard;

namespace ExamApp.Api.Services.QuestionTransfer;

/// <summary>
/// Development-only Hangfire dashboard access: any authenticated user can view.
/// Keep production locked down via <see cref="HangfireDashboardAuthFilter"/>.
/// </summary>
public class HangfireDashboardDevAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User?.Identity?.IsAuthenticated == true;
    }
}
