using System;
using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers;

[Authorize]
public class BaseController : ControllerBase
{    
    protected string KeyCloakId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    protected async Task<User> GetAuthenticatedUserAsync()
    {
        var db = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        return await db.Users.FirstOrDefaultAsync(u => u.KeycloakId == KeyCloakId) ?? throw new UnauthorizedAccessException("User not found.");
    }
}