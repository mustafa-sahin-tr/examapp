using System.Security.Claims;
using Hangfire.Dashboard;

namespace ExamApp.Api.Services.QuestionTransfer;

/// <summary>
/// Production Hangfire dashboard erişimi: yalnız Admin/SuperAdmin. issue #365 (S1): dashboard tüm işlerin argümanlarını
/// kiracılar arası gösterir; daha önce açık olan onaylı öğretmen erişimi (#287) kaldırıldı. Rol listesi
/// <see cref="ExamApp.Api.Controllers.HangfireSessionController"/> ile senkron kalmalı.
/// </summary>
public class HangfireDashboardAuthFilter : IDashboardAsyncAuthorizationFilter
{
    // Roller /hangfire cookie'sindeki principal anlık görüntüsünden gelir (login anı, 30 dk sliding) — her istekte canlı
    // DB/Keycloak kontrolü yok; rolü alınan admin cookie süresi dolana kadar erişebilir.
    public Task<bool> AuthorizeAsync(DashboardContext context) => Task.FromResult(IsAllowed(context.GetHttpContext().User));

    internal static bool IsAllowed(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true && (user.IsInRole("Admin") || user.IsInRole("SuperAdmin"));
}
