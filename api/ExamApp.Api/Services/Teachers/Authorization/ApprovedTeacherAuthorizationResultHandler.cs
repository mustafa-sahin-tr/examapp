using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Teachers;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Teachers.Authorization;

/// <summary>
/// issue #287: yalnızca <see cref="ApprovedTeacherRequirement"/> yüzünden reddedilen isteklere UI'ın ayırt edebileceği
/// 403 gövdesi yazar: <c>{ success:false, errorCode:"TeacherNotApproved", message }</c>. Başka bir gereksinim de
/// başarısızsa (ör. rol yok) ya da kimlik yoksa varsayılan davranış (gövdesiz 401/403) aynen korunur.
/// <para>
/// Pipeline'da <c>UseRequestLocalization</c>, <c>UseAuthorization</c>'dan SONRA çalışır (kullanıcı dil tercihi claim
/// ister, bkz. Program.cs). Bu yüzden mesaj dili burada aynı <see cref="RequestLocalizationOptions"/> provider'larıyla
/// çözülür — controller'lara ulaşan yanıtlarla aynı dil.
/// </para>
/// </summary>
public sealed class ApprovedTeacherAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!IsTeacherNotApproved(authorizeResult))
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        // Async-local: yalnızca bu metodun akışını etkiler; metot dönünce çağıranın kültürü geri gelir.
        if (await ResolveRequestCultureAsync(context) is { } culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        var localizer = context.RequestServices.GetRequiredService<IStringLocalizer<Messages>>();

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new TeacherNotApprovedResponseDto
        {
            Success = false,
            ErrorCode = TeacherAccessErrorCodes.TeacherNotApproved,
            Message = localizer["teacher.notApproved"].Value
        }, context.RequestAborted);
    }

    /// <summary>Yasaklandı VE başarısız gereksinimlerin tamamı <see cref="ApprovedTeacherRequirement"/>.</summary>
    internal static bool IsTeacherNotApproved(PolicyAuthorizationResult result)
    {
        if (!result.Forbidden)
            return false;

        var failed = result.AuthorizationFailure?.FailedRequirements.ToList();
        return failed is { Count: > 0 } && failed.All(r => r is ApprovedTeacherRequirement);
    }

    /// <summary>RequestLocalization middleware'inin seçeceği UI kültürü (provider sırası + desteklenen kültürler).</summary>
    internal static async Task<CultureInfo?> ResolveRequestCultureAsync(HttpContext context)
    {
        var options = context.RequestServices.GetService<IOptions<RequestLocalizationOptions>>()?.Value;
        if (options == null)
            return null;

        var culture = options.DefaultRequestCulture.UICulture;
        foreach (var provider in options.RequestCultureProviders)
        {
            var result = await provider.DetermineProviderCultureResult(context);
            var match = result?.UICultures
                .Select(c => options.SupportedUICultures?.FirstOrDefault(s =>
                    string.Equals(s.Name, c.Value, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefault(c => c != null);
            if (match != null)
                return match;
        }

        return culture;
    }
}
