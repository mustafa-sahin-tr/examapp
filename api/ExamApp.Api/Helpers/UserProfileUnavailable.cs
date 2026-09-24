using System;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #277 security re-review: kullanıcı token'ı için profil (Redis/auth-api) çözülemedi. Eskiden
/// <c>BaseController.GetAuthenticatedUserAsync</c> bu durumda <c>Role="Service"</c>'li sahte bir profil üretiyordu ve
/// servis-hesabı muafiyeti olan kontroller (ör. StudyItemService sahiplik kontrolü) atlanıyordu. Artık fail-closed:
/// bu istisna fırlatılır ve <see cref="UserProfileUnavailableFilterAttribute"/> ile 503'e çevrilir; hiçbir iş kuralı çalışmaz.
/// </summary>
public sealed class UserProfileUnavailableException(Exception inner)
    : Exception("User profile could not be resolved (profile provider failure).", inner);

/// <summary>
/// <see cref="UserProfileUnavailableException"/> → <c>503 { message }</c> (yerelleştirilmiş <c>common.profileUnavailable</c>).
/// BaseController'a uygulanır (miras alınır); iç hata loglanır, istemciye sızdırılmaz.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class UserProfileUnavailableFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not UserProfileUnavailableException ex)
            return;

        var services = context.HttpContext.RequestServices;
        services.GetService<ILoggerFactory>()?.CreateLogger("ExamApp.Api.UserProfile")
            .LogError(ex.InnerException, "User profile provider failed; request rejected with 503 (fail-closed).");

        var localizer = services.GetService<IStringLocalizer<Messages>>() ?? FallbackMessageLocalizer.Instance;
        context.Result = new ObjectResult(new { message = localizer["common.profileUnavailable"].Value })
        {
            StatusCode = StatusCodes.Status503ServiceUnavailable
        };
        context.ExceptionHandled = true;
    }
}
