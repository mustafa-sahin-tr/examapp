using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Teachers.Authorization;

/// <summary>
/// issue #287: öğretmen özelliklerini yalnızca hesabı admin tarafından onaylanmış öğretmenlere açan policy adları.
/// Rol attribute'unun (<c>[Authorize(Roles = "Teacher")]</c> vb.) YERİNE değil, YANINA konur — iki attribute tek
/// policy'de birleşir (AND).
/// </summary>
public static class ApprovedTeacherPolicies
{
    /// <summary>
    /// Öğretmen-yalnız, "Teacher,Admin" ve öğretmen dalı olan genel uçlar. Çağıran Teacher rolündeyse hesabı onaylı
    /// olmalı; Admin/SuperAdmin, servis hesabı ve Teacher rolü olmayan herkes muaf.
    /// </summary>
    public const string TeacherCapability = "ApprovedTeacher";

    /// <summary>
    /// "Teacher,Student" uçları ve öğrenci akışıyla paylaşılan genel uçlar. Öğrenci (Teacher rolü olmayan) zaten
    /// gereksinimin kapsamı dışında olduğu için geçer. security review L4: çağıranda HEM Teacher HEM Student rolü varsa
    /// Student muafiyet SAĞLAMAZ — onaysız öğretmen öğrenci rolüyle kapıyı aşamaz. Davranış bu yüzden
    /// <see cref="TeacherCapability"/> ile aynıdır; ayrı ad, ucun öğrencilerle paylaşıldığını belgelemek için korunur.
    /// </summary>
    public const string TeacherOrStudentCapability = "ApprovedTeacherOrStudent";

    internal static readonly string[] AdminRoles = { "Admin", "SuperAdmin" };
}

/// <summary>issue #287: "Teacher rolündeki çağıranın öğretmen hesabı onaylı olmalı" gereksinimi.</summary>
public sealed class ApprovedTeacherRequirement : IAuthorizationRequirement
{
    public const string TeacherRole = "Teacher";

    public ApprovedTeacherRequirement(params string[] exemptRoles)
    {
        ExemptRoles = exemptRoles ?? Array.Empty<string>();
    }

    /// <summary>Bu rollerden birine sahip çağıran gereksinimden muaftır (Admin, SuperAdmin).</summary>
    public IReadOnlyList<string> ExemptRoles { get; }

    /// <summary>Bu gereksinim çağıran için geçerli mi? Teacher rolü yoksa ya da muaf bir rolü varsa false.</summary>
    public bool AppliesTo(ClaimsPrincipal user) =>
        user.IsInRole(TeacherRole) && !ExemptRoles.Any(user.IsInRole);
}

/// <summary>
/// issue #287: <see cref="ApprovedTeacherRequirement"/>'ı <see cref="IApprovedTeacherGuard"/> ile değerlendirir.
/// Scoped: guard istek başına sonucu önbellekler; aynı istekte servis katmanı (ör. çalışma linkleri) tekrar sorgulamaz.
/// Profil çözümü fail-closed'dur (BaseController ile aynı): provider fırlatırsa istek 500 ile düşer, sessizce izin verilmez.
/// </summary>
public sealed class ApprovedTeacherAuthorizationHandler : AuthorizationHandler<ApprovedTeacherRequirement>
{
    private readonly IApprovedTeacherGuard _guard;
    private readonly IUserProfileProvider _profiles;
    private readonly string[]? _serviceClients;
    private readonly ILogger<ApprovedTeacherAuthorizationHandler> _logger;

    public ApprovedTeacherAuthorizationHandler(IApprovedTeacherGuard guard, IUserProfileProvider profiles,
        IConfiguration configuration, ILogger<ApprovedTeacherAuthorizationHandler> logger)
    {
        _guard = guard;
        _profiles = profiles;
        _serviceClients = configuration.GetSection("Keycloak:ServiceClients").Get<string[]>();
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ApprovedTeacherRequirement requirement)
    {
        var user = context.User;
        if (!requirement.AppliesTo(user) || ServicePrincipal.IsService(user, _serviceClients))
        {
            context.Succeed(requirement);
            return;
        }

        var ct = (context.Resource as HttpContext)?.RequestAborted ?? default;
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier);
        UserProfileDto? profile;
        try
        {
            profile = string.IsNullOrEmpty(sub) ? null : await _profiles.GetAsync(sub, ct);
        }
        catch (Exception ex) when (IsCallerIdentityFailure(ex))
        {
            // Sağlayıcı kesintisi değil, çağıranın kimlik bağlamı sorunlu: istekte iletilecek token yok, auth-api token'ı
            // reddetti (401/403 — ör. süresi dolmuş token) ya da dönen profil doğrulanan sub'a ait değil. 500 yerine
            // yetkisiz (403, TeacherNotApproved gövdesi olmadan — Fail çağrıldığı için). Diğer hatalar fail-closed fırlar.
            _logger.LogWarning("ApprovedTeacher: profil çağıranın kimliğiyle yüklenemedi ({Reason}). Sub={Sub}",
                ex.GetType().Name, sub);
            context.Fail(new AuthorizationFailureReason(this, "Caller identity could not be used to load the user profile."));
            return;
        }
        var check = profile is { Id: > 0 }
            ? await _guard.CheckAsync(profile.Id, ct)
            : TeacherApprovalCheck.NoTeacherProfile;

        if (check == TeacherApprovalCheck.Approved)
        {
            context.Succeed(requirement);
            return;
        }

        _logger.LogInformation("Onaysız öğretmen öğretmen özelliğine erişmeye çalıştı. UserId={UserId}, Check={Check}",
            profile?.Id, check);
        // Bilerek context.Fail() ÇAĞRILMAZ: Fail çağrılınca AuthorizationFailure yalnızca FailureReasons taşır,
        // FailedRequirements boş kalır. Gereksinimi karşılanmamış bırakmak (pending) sonucu yine Forbidden yapar ve
        // result handler "yalnızca bu gereksinim mi başarısız?" sorusunu (rol eksikliğinden ayırarak) cevaplayabilir.
    }

    /// <summary>Çağıranın kimlik bağlamından kaynaklanan (sağlayıcı kesintisi olmayan) profil yükleme hataları.</summary>
    private static bool IsCallerIdentityFailure(Exception ex) => ex switch
    {
        CallerAccessTokenMissingException => true,
        UserProfileSubjectMismatchException => true,
        System.Net.Http.HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden } => true,
        _ => false
    };
}
