using System;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentInvitations;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Parents.Enforcement;

/// <summary>Issue #440 / #441 yaptırım policy adları.</summary>
public static class ParentRequirementPolicies
{
    /// <summary>
    /// #440: veli daveti aktive edilmemiş öğrenciye kapalı uçlar (booking talebi, direkt mesaj gönderme, herkese açık yorum, liderlik
    /// tablosu). Rol attribute'unun YANINA konur; yalnız veli kuralına tabi çağıranı etkiler (öğretmen/admin/veli/servis geçer).
    /// </summary>
    public const string ParentActivated = "ParentActivated";
}

/// <summary>#440: "veli kuralına tabi çağıranın açık (aktive edilmemiş) daveti olmamalı" gereksinimi.</summary>
public sealed class ParentActivationRequirement : IAuthorizationRequirement;

/// <summary>
/// #441: hoşgörü süresi dolmuş (<see cref="StudentParentMode.Expired"/>) öğrencinin de çağırabileceği uç ("veli bilgisi ekle"
/// akışının ihtiyaç duyduğu profil/oturum/veli uçları). İşaretsiz her yetkili uç 403 <c>ParentRequired</c> döner.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AllowWhenParentRequiredAttribute : Attribute;

/// <summary>Kimliği doğrulanmış çağıranın veli durumu — istek başına bir kez çözülür (policy handler + middleware paylaşır).</summary>
public interface IStudentParentStatusResolver
{
    /// <summary>Çağıran veli kuralına tabi değilse (yetişkin rolü, servis, anonim) ya da profil çözülemezse null.</summary>
    Task<StudentParentState?> ResolveAsync(ClaimsPrincipal user, CancellationToken ct = default);
}

/// <inheritdoc cref="IStudentParentStatusResolver"/>
public sealed class StudentParentStatusResolver : IStudentParentStatusResolver
{
    /// <summary>Metrik: veli durumu çözülemediği için yaptırımın uygulanmadığı (fail-open) istek sayısı.</summary>
    public const string MeterName = "ExamApp.ParentRequirement";
    public const string FailOpenCounterName = "parent_requirement.status_unresolved";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> FailOpen = Meter.CreateCounter<long>(FailOpenCounterName,
        description: "Requests where the student's parent status could not be resolved (profile provider down); enforcement skipped.");

    private readonly IUserProfileProvider _profiles;
    private readonly IStudentParentStatusService _status;
    private readonly string[]? _serviceClients;
    private readonly ILogger<StudentParentStatusResolver> _logger;
    private bool _resolved;
    private StudentParentState? _state;

    public StudentParentStatusResolver(IUserProfileProvider profiles, IStudentParentStatusService status, IConfiguration configuration,
        ILogger<StudentParentStatusResolver> logger)
    {
        _profiles = profiles;
        _status = status;
        _serviceClients = configuration.GetSection("Keycloak:ServiceClients").Get<string[]>();
        _logger = logger;
    }

    public async Task<StudentParentState?> ResolveAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (_resolved)
            return _state;
        _resolved = true;

        if (user.Identity?.IsAuthenticated != true || !ParentRequirement.RequiresParent(user) || ServicePrincipal.IsService(user, _serviceClients))
            return _state = null;

        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
            return _state = null;

        try
        {
            var profile = await _profiles.GetAsync(sub, ct);
            if (profile is not { Id: > 0 })
                return _state = null;
            return _state = await _status.GetAsync(profile.Id, requiresParent: true, ct);
        }
        catch (Exception ex) when (IsProfileProviderFailure(ex, ct))
        {
            // Review MINOR1: YALNIZ profil sağlayıcı (auth-api) / kimlik bağlamı / zaman aşımı hataları fail-open — ürün kuralı, veri
            // sızıntısı değil; uçların kendisi profil çözemediğinde zaten 503/404 döner. DB hataları (durum sorgusu) yutulmaz.
            FailOpen.Add(1);
            _logger.LogWarning(ex, "[ParentRequirement] Veli durumu çözülemedi; yaptırım bu istekte uygulanmadı (metric {Metric}). Sub={Sub}",
                FailOpenCounterName, sub);
            return _state = null;
        }
    }

    private static bool IsProfileProviderFailure(Exception ex, CancellationToken ct) => ex switch
    {
        System.Net.Http.HttpRequestException => true,
        TimeoutException => true,
        System.Text.Json.JsonException => true,
        UserProfileUnavailableException => true,
        UserProfileSubjectMismatchException => true,
        CallerAccessTokenMissingException => true,
        OperationCanceledException => !ct.IsCancellationRequested,
        _ => false
    };

}

/// <summary>#440: <see cref="ParentActivationRequirement"/> — mod AwaitingActivation ise karşılanmaz (403 <c>ParentActivationRequired</c>).</summary>
public sealed class ParentActivationAuthorizationHandler(IStudentParentStatusResolver resolver) : AuthorizationHandler<ParentActivationRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ParentActivationRequirement requirement)
    {
        var ct = (context.Resource as HttpContext)?.RequestAborted ?? default;
        var state = await resolver.ResolveAsync(context.User, ct);
        if (state?.Mode == StudentParentMode.AwaitingActivation)
            return; // bilerek Fail değil: result handler başarısız gereksinimi ayırt edebilsin (#287 ile aynı)
        context.Succeed(requirement);
    }
}

/// <summary>
/// Yetkilendirme sonuç işleyicisi (tekil): yalnız <see cref="ParentActivationRequirement"/> başarısızsa
/// <c>{ success:false, errorCode:"ParentActivationRequired", message }</c> 403; aksi her durum #287'nin
/// <see cref="ApprovedTeacherAuthorizationResultHandler"/>'ına (TeacherNotApproved / varsayılan) devredilir.
/// </summary>
public sealed class ParentRequirementAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly ApprovedTeacherAuthorizationResultHandler _inner = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!IsParentActivationRequired(authorizeResult))
        {
            await _inner.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (await ApprovedTeacherAuthorizationResultHandler.ResolveRequestCultureAsync(context) is { } culture)
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = culture;
        }
        await ParentRequirementResponses.WriteAsync(context, ParentRequirementErrorCodes.ParentActivationRequired);
    }

    internal static bool IsParentActivationRequired(PolicyAuthorizationResult result)
    {
        if (!result.Forbidden)
            return false;
        var failed = result.AuthorizationFailure?.FailedRequirements.ToList();
        return failed is { Count: > 0 } && failed.All(r => r is ParentActivationRequirement);
    }
}

/// <summary>Ortak 403 gövdesi yazıcısı.</summary>
internal static class ParentRequirementResponses
{
    public static Task WriteAsync(HttpContext context, string errorCode)
    {
        var localizer = context.RequestServices.GetService<IStringLocalizer<Messages>>() ?? FallbackMessageLocalizer.Instance;
        var key = errorCode == ParentRequirementErrorCodes.ParentRequired
            ? "parentInvitations.errors.parentRequired"
            : "parentInvitations.errors.parentActivationRequired";
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(new ParentRequirementErrorResponseDto
        {
            Success = false,
            ErrorCode = errorCode,
            Message = localizer[key].Value
        }, context.RequestAborted);
    }
}

/// <summary>
/// #441: hoşgörü süresi dolmuş öğrenci (mod <see cref="StudentParentMode.Expired"/>) için yetkili TÜM uçlarda 403 <c>ParentRequired</c> —
/// yalnız <see cref="AllowWhenParentRequiredAttribute"/> işaretli uçlar (oturum, profil, veli durumu/ekleme) açık. Anonim uçlar
/// (<c>[AllowAnonymous]</c> ya da yetki verisi olmayan) ve veli kuralına tabi olmayan çağıranlar (öğretmen/admin/veli/servis) etkilenmez.
/// Veri silinmez. Pipeline'da UseAuthorization + UseRequestLocalization'dan SONRA (mesaj dili), UseRateLimiter'dan ÖNCE.
/// </summary>
public sealed class ParentRequiredMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IStudentParentStatusResolver resolver)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint == null
            || context.User.Identity?.IsAuthenticated != true
            || endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null
            || endpoint.Metadata.GetMetadata<IAuthorizeData>() == null
            || endpoint.Metadata.GetMetadata<AllowWhenParentRequiredAttribute>() != null)
        {
            await next(context);
            return;
        }

        var state = await resolver.ResolveAsync(context.User, context.RequestAborted);
        if (state?.Mode == StudentParentMode.Expired)
        {
            await ParentRequirementResponses.WriteAsync(context, ParentRequirementErrorCodes.ParentRequired);
            return;
        }

        await next(context);
    }
}

public static class ParentRequirementEnforcementServiceCollectionExtensions
{
    /// <summary>
    /// #440 / #441 yaptırımı: <see cref="ParentRequirementPolicies.ParentActivated"/> policy'si + handler + tekil sonuç işleyicisi
    /// (#287 işleyicisini sarar — <c>AddApprovedTeacherAuthorization()</c>'dan SONRA çağrılmalı) + istek başı durum çözücüsü.
    /// </summary>
    public static IServiceCollection AddParentRequirementEnforcement(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
            options.AddPolicy(ParentRequirementPolicies.ParentActivated, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new ParentActivationRequirement())));

        services.AddScoped<IStudentParentStatusResolver, StudentParentStatusResolver>();
        services.AddScoped<IAuthorizationHandler, ParentActivationAuthorizationHandler>();
        services.RemoveAll<IAuthorizationMiddlewareResultHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ParentRequirementAuthorizationResultHandler>();
        return services;
    }

    public static IApplicationBuilder UseParentRequirementEnforcement(this IApplicationBuilder app)
        => app.UseMiddleware<ParentRequiredMiddleware>();
}
