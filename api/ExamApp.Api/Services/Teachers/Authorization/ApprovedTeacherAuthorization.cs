using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
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
    /// "Teacher,Student" uçları ve öğrenci akışıyla paylaşılan genel uçlar: <see cref="TeacherCapability"/> + Student
    /// rolü de muaf (öğrenci yeteneği kullanılıyor; exam API öğretmen/öğrenci kaydını birbirini dışlar, #234).
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

    /// <summary>Bu rollerden birine sahip çağıran gereksinimden muaftır (ör. Admin; karma uçlarda Student).</summary>
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
        var profile = string.IsNullOrEmpty(sub) ? null : await _profiles.GetAsync(sub, ct);
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
}
