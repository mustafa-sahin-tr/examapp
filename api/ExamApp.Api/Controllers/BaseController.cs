using System;
using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Constants;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Controllers;

[Authorize]
public class BaseController : ControllerBase
{
    protected string? KeyCloakId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    protected bool IsServiceAccount =>
        ExamApp.Foundation.Security.ServicePrincipal.IsService(
            User,
            HttpContext.RequestServices.GetRequiredService<IConfiguration>()
                .GetSection("Keycloak:ServiceClients").Get<string[]>());

    /// <summary>
    /// issue #189: admin muafiyeti bu bayrağa bakılarak verilmelidir — GetCurrentSchoolIdAsync'in
    /// null dönmesi "okulsuz kullanıcı" ile "admin/servis hesabı" anlamlarını ayırt etmez.
    /// </summary>
    protected bool IsAdmin => User.IsInRole("Admin");

    protected Task<UserProfileDto> GetAuthenticatedUserAsync() => GetAuthenticatedUserAsync(CancellationToken.None);

    protected async Task<UserProfileDto> GetAuthenticatedUserAsync(CancellationToken ct)
    {
        var preferredUsername = User.FindFirstValue("preferred_username");

        if (IsServiceAccount)
        {
            return new UserProfileDto
            {
                Id = 0,
                KeycloakId = KeyCloakId ?? string.Empty,
                FullName = preferredUsername ?? "Service Account",
                Email = string.Empty,
                Role = "Service"
            };
        }

        var userProfileProvider = HttpContext.RequestServices.GetRequiredService<IUserProfileProvider>();
        try
        {
            return await userProfileProvider.GetAsync(KeyCloakId, ct);
        }
        catch
        {
            return new UserProfileDto
            {
                Id = 0,
                KeycloakId = KeyCloakId ?? string.Empty,
                FullName = preferredUsername ?? User.Identity?.Name ?? "Authenticated User",
                Email = string.Empty,
                Role = "Service"
            };
        }
    }

    /// <summary>
    /// issue #189: JWT'deki school_id claim'i — yalnızca ipucudur, tek başına yetki kararı vermez.
    /// Geçersiz/yok ise null. Yetki kararları için GetCurrentSchoolIdAsync (sunucu tarafı DB
    /// doğrulaması) kullanılmalı.
    /// </summary>
    protected int? SchoolIdClaimHint =>
        int.TryParse(User.FindFirstValue(ExamClaimTypes.SchoolId), out var schoolId) ? schoolId : null;

    /// <summary>
    /// "CurrentSchoolId": kullanıcının sunucu tarafında (Teacher/Student tablosundan) doğrulanmış
    /// okul kimliği. null = okulsuz/bağımsız kullanıcı — admin veya servis hesabı olduğu için değil.
    /// Çağıran taraf admin muafiyetini bu metodun null dönüşüne değil, <see cref="IsAdmin"/>'e
    /// bakarak ayrıca vermeli. Bu değer TEK BAŞINA yetki kararı vermez. Client'tan (query/body)
    /// gelen schoolId parametresine ASLA güvenme; yetki her zaman bu metodun döndürdüğü değere
    /// dayanmalı. Claim (SchoolIdClaimHint) ile DB değeri uyuşmazsa DB değeri kazanır ve
    /// uyuşmazlık loglanır.
    ///
    /// Güvenlik notu (fail-closed): bu metod Redis/auth-api kesintisinde sessizce null DÖNMEZ —
    /// profil çözülemezse exception fırlatır (çağıran 500 almalı). Aksi halde (fail-open) her
    /// kullanıcı yanlışlıkla "okulsuz" sayılıp okul bazlı filtreler bypass edilebilirdi.
    /// </summary>
    protected async Task<int?> GetCurrentSchoolIdAsync(CancellationToken ct = default)
    {
        if (IsServiceAccount || IsAdmin)
            return null;

        var userProfileProvider = HttpContext.RequestServices.GetRequiredService<IUserProfileProvider>();
        var user = await userProfileProvider.GetAsync(KeyCloakId, ct)
            ?? throw new InvalidOperationException(
                $"User profile could not be resolved for KeycloakId={KeyCloakId}; cannot determine school context.");

        var dbSchoolId = user.SchoolId;

        var claimHint = SchoolIdClaimHint;
        if (claimHint.HasValue && claimHint != dbSchoolId)
        {
            var logger = HttpContext.RequestServices.GetRequiredService<ILogger<BaseController>>();
            logger.LogWarning(
                "SchoolId claim mismatch for KeycloakId={KeycloakId}: claim={ClaimSchoolId}, db={DbSchoolId}. DB değeri kullanılıyor.",
                KeyCloakId, claimHint, dbSchoolId);
        }

        return dbSchoolId;
    }
}