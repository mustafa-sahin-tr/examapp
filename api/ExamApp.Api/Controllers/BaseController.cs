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

namespace ExamApp.Api.Controllers;

[Authorize]
public class BaseController : ControllerBase
{
    protected string? KeyCloakId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    protected bool IsServiceAccount
    {
        get
        {
            var preferredUsername = User.FindFirstValue("preferred_username");
            // exam-admin client is treated as god/service user
            return preferredUsername?.Equals("exam-admin", StringComparison.OrdinalIgnoreCase) == true;
        }
    }

    protected async Task<UserProfileDto> GetAuthenticatedUserAsync()
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

        var userProfileCacheService = HttpContext.RequestServices.GetRequiredService<UserProfileCacheService>();
        try
        {
            return await userProfileCacheService.GetOrSetAsync(KeyCloakId, async () =>
            {
                var authApiClient = HttpContext.RequestServices.GetRequiredService<IAuthApiClient>();
                return await authApiClient.GetUserProfileAsync();
            });
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
}