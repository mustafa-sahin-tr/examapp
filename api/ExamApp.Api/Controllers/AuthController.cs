using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IJwtService _jwtService;
        
        protected readonly AppDbContext _context;
        public AuthController(AppDbContext context, IJwtService jwtService)
            : base()
        {
            _jwtService = jwtService;
            _context = context;
        }

        [Authorize]
        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshToken()
        {
            // 1) Token içindeki Sub claim (user.Id) alınır
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdString, out var userId))
                return Unauthorized();

            // 2) Veritabanından en güncel user + ilişkili Student/Teacher/Parent yüklenir
            var user = await _context.Users
                .Include(u => u.Student)
                .Include(u => u.Teacher)
                .Include(u => u.Parent)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return Unauthorized();

            // 3) Yeni JWT oluşturulur (artık Student doluysa StudentId claim’i eklenecek)
            var newToken = _jwtService.GenerateToken(user);

            // 4) İsterseniz role ve avatar gibi bilgileri de dönersiniz
            return Ok(new
            {
                Token  = newToken,
                Role   = user.Role.ToString(),
                Avatar = user.AvatarUrl
            });
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
            return Ok(new { Token = token,
                Role = user.Role.ToString() ,
                User = new { user.Id, user.FullName, user.Email },
                Avatar = user.AvatarUrl });
        }

    }
}
