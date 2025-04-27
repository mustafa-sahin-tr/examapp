using System;
using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers;

[ExamAuthorize]
public class BaseController : ControllerBase
{    
    protected int AuthenticatedUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
    protected int AuthenticatedStudentId => int.Parse(User.FindFirstValue(ExamClaimTypes.StudentId) ?? "0");
    protected int AuthenticatedTeacherId => int.Parse(User.FindFirstValue(ExamClaimTypes.TeacherId) ?? "0");

    protected async Task<User> GetAuthenticatedUserAsync()
    {
        var db = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        return await db.Users.FindAsync(AuthenticatedUserId) ?? throw new UnauthorizedAccessException("User not found.");
    }

    protected async Task<Student?> GetAuthenticatedStudentAsync()
    {
        var db = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        return await db.Students.FindAsync(AuthenticatedStudentId);
    }

    protected async Task<Teacher?> GetAuthenticatedTeacherAsync()
    {
        var db = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var teacherId = AuthenticatedTeacherId;
        return await db.Teachers.FindAsync(teacherId);
    }
}