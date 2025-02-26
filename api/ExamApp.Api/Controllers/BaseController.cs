using System;
using System.Security.Claims;
using ExamApp.Api.Data;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

public class BaseController : ControllerBase
{
    protected readonly AppDbContext _context;

    protected int AuthenticatedUserId
    {
        get
        {
            var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return userIdValue != null ? int.Parse(userIdValue) : throw new UnauthorizedAccessException("User ID not found.");
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
    
    protected async Task<User> GetAuthenticatedUserAsync()
    {        
        var user = await _context.Users.FindAsync(AuthenticatedUserId);
        return user ?? throw new UnauthorizedAccessException("Authenticated user not found.");
    }    

    protected async Task<Student> GetAuthenticatedStudentIdAsync()
    {
        
        var student = await _context.Students.FindAsync(AuthenticatedStudentId);
        if (student == null)
        {
            throw new UnauthorizedAccessException("Student not found.");
        }

        return student;
    }
}