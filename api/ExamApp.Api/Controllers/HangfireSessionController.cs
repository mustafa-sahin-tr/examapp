using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Hangfire dashboard (<c>/hangfire</c>) için tarayıcı cookie oturumu. issue #365 (S1): QuestionTransferController
/// Admin-only olunca buraya taşındı (URL aynı: <c>api/question-transfer/hangfire/*</c>) ve yalnız Admin/SuperAdmin'e
/// daraltıldı — dashboard tüm işlerin argümanlarını kiracılar arası gösterir. Dashboard'un kendisi
/// <see cref="ExamApp.Api.Services.QuestionTransfer.HangfireDashboardAuthFilter"/> ile aynı rollerle korunur; senkron kalmalı.
/// </summary>
[ApiController]
[Route("api/question-transfer/hangfire")]
[Authorize(Roles = DashboardRoles)]
public class HangfireSessionController : ControllerBase
{
    internal const string DashboardRoles = "Admin,SuperAdmin";

    // Creates an HttpOnly cookie scoped to /hangfire so the dashboard can be opened in a browser.
    [HttpPost("login")]
    public async Task<IActionResult> HangfireLogin()
    {
        await HttpContext.SignInAsync(
            "HangfireCookie",
            User,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
            });

        return Ok(new { message = "Hangfire session created" });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> HangfireLogout()
    {
        await HttpContext.SignOutAsync("HangfireCookie");
        return Ok(new { message = "Hangfire session cleared" });
    }
}
