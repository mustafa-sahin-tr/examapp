using Hangfire.Dashboard;

namespace ExamApp.Api.Services.QuestionTransfer;

public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        // Most secure default: only teacher/admin can view Hangfire.
        return httpContext.User.IsInRole("Teacher")
            || httpContext.User.IsInRole("Admin")
            || httpContext.User.IsInRole("SuperAdmin");
    }
}
