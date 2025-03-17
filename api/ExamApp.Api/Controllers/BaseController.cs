using System;
using System.Security.Claims;
using ExamApp.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ExamApp.Api.Controllers;

[ExamAuthorize]
public class BaseController : ControllerBase
{    
    protected readonly AppDbContext _context;

    protected int AuthenticatedUserId
    {
        get
        {            
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
               return userIdValue != null ? int.Parse(userIdValue) :
                 throw new UnauthorizedAccessException("User ID not found.");
        }
    }

    /// <summary>
    /// Read user Id without throwing exception if not found.
    /// </summary>
    private int UserId {
        get
        {                        
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdClaim, out int userId))
                {
                    return userId;
                }
            }
            return 0;  // Kullanıcı yoksa veya kimliği çözülemiyorsa 0 döndür                
                }
    } 

    protected int AuthenticatedStudentId
    {
        get
        {
            var studentIdValue = User.FindFirstValue("StudentId");
            return studentIdValue != null ? int.Parse(studentIdValue) : throw new UnauthorizedAccessException("Student ID not found.");
        }
    }
    public BaseController(AppDbContext context)
    {
        _context = context;    
    }

    // public override void OnActionExecuting(ActionExecutingContext context)
    // {
    //     base.OnActionExecuting(context);
        
    //     CurrentUserId = GetCurrentUserId();
    //     _dbContext.SetCurrentUser(CurrentUserId ?? 0);  // DbContext içine UserId'yi set et
    // }
    
    protected async Task<User> GetAuthenticatedUserAsync()
    {                
        var user = await _context.Users.FindAsync(AuthenticatedUserId);
        return user ?? throw new UnauthorizedAccessException("Authenticated user not found.");
    }    

    protected async Task<Student> GetAuthenticatedStudentAsync()
    {
        var student = await _context.Students.FindAsync(AuthenticatedStudentId);
        if (student == null)
        {
            throw new UnauthorizedAccessException("Student not found.");
        }
        return student;
    }
}