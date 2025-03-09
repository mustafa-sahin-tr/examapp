using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : BaseController
    {
        private readonly IJwtService _jwtService;


        public AuthController(AppDbContext context, IJwtService jwtService)
            : base(context)
        {
            _jwtService = jwtService;
        }


        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto request)
        {
            if (_context.Users.Any(u => u.Email == request.Email))
            {
                return BadRequest("Email already exists.");
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
            
            var user = new User
            {
                FullName = request.FullName,
                Email = request.Email,
                PasswordHash = hashedPassword,
                Role = request.Role
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(user);
        }


        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto request)
        {
            var user = await _context.Users
                .Include(u => u.Student)
                .Include(u => u.Teacher)
                .Include(u => u.Parent)
            .FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Unauthorized("Invalid email or password.");
            }

            var token = _jwtService.GenerateToken(user);
            return Ok(new { Token = token, Role = user.Role.ToString() , Avatar = user.AvatarUrl });
        }

    }
}
