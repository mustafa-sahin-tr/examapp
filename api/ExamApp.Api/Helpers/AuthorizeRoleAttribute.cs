using ExamApp.Api.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class AuthorizeRoleAttribute : Attribute, IAsyncActionFilter
{
    private readonly UserRole[] _roles;

    public AuthorizeRoleAttribute(params UserRole[] roles)
    {
        _roles = roles;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;

        if (httpContext.User == null || !httpContext.User.Identity.IsAuthenticated)
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.HttpContext.Response.WriteAsync("Unauthorized");
            return;
        }

        // Kullanıcının rolleri claim'lerden alınıyor (örneğin, ClaimTypes.Role kullanıyorsanız)
        var roleClaim = httpContext.User.FindFirstValue(ClaimTypes.Role);

        if (string.IsNullOrEmpty(roleClaim))
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.HttpContext.Response.WriteAsync("Forbidden: No role assigned.");
            return;
        }

        // Kullanıcının rolü enum ile eşleşiyor mu?
        if (!_roles.Any(r => r.ToString() == roleClaim))
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.HttpContext.Response.WriteAsync("Forbidden: You do not have the required role.");
            return;
        }

        // **DbContext'i Dependency Injection ile al**
        var dbContext = httpContext.RequestServices.GetRequiredService<AppDbContext>();

        // **UserId'yi al ve DbContext içine set et**
        var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdClaim, out int userId))
        {
            dbContext.SetCurrentUser(userId);
        }
        else
        {
            dbContext.SetCurrentUser(0); // Kullanıcı yoksa 0 ata
        }

        await next();
    }
}


[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ExamAuthorizeAttribute : Attribute, IAsyncActionFilter
{
   

    public ExamAuthorizeAttribute()
    {
        
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {

        var httpContext = context.HttpContext;

        if (httpContext.User == null || !httpContext.User.Identity.IsAuthenticated)
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.HttpContext.Response.WriteAsync("Unauthorized");
            return;
        }

        var dbContext = httpContext.RequestServices.GetRequiredService<AppDbContext>();
        var userIdClaim = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(userIdClaim, out int userId))
        {
            dbContext.SetCurrentUser(userId);
        }

        await next();
    }
}
