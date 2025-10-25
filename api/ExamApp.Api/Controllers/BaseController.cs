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
    protected string KeyCloakId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    protected async Task<UserProfileDto> GetAuthenticatedUserAsync()
    {
        var userProfileCacheService = HttpContext.RequestServices.GetRequiredService<UserProfileCacheService>();
        // authenticated user'ı auth-service üzerinden al
        return await userProfileCacheService.GetOrSetAsync(KeyCloakId, async () =>
        {
            var authApiClient = HttpContext.RequestServices.GetRequiredService<IAuthApiClient>();
            return await authApiClient.GetUserProfileAsync();
        });
        
    }
}