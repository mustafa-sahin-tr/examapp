using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;

namespace ExamApp.Api.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _context;

    public UserService(AppDbContext context)
    {
        _context = context;
    }
    public async Task<ResponseBaseDto> UpdateUserAvatar(int userId, string avatarUrl) 
    {
        var user = await _context.Users.FindAsync(userId);
        user.AvatarUrl = avatarUrl;
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return new ResponseBaseDto
        {
            Success = true,
            Message = "Avatar updated successfully"
        };
    }
}
