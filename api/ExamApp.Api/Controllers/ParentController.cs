using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ParentController : BaseController
    {
        private readonly AppDbContext _context;
        private readonly IKeycloakService _keycloakService;

        public ParentController(AppDbContext context, IKeycloakService keycloakService) : base()
        {
            _context = context;
            _keycloakService = keycloakService;
        }

        // Veli kaydı: profil alanı yok — sadece realm rolünü ata ve Parent satırını aç.
        [Authorize]
        [HttpPost("register")]
        public async Task<IActionResult> RegisterParent()
        {
            var user = await GetAuthenticatedUserAsync();
            if (user == null || user.Id <= 0)
            {
                return Unauthorized(new { message = "Kullanıcı çözümlenemedi." });
            }

            await _keycloakService.SetRoleAsync(user.KeycloakId, UserRole.Parent);

            var refreshToken = Request.Cookies["refresh_token"];
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Unauthorized("No refresh token provided.");

            var tokenData = await _keycloakService.RefreshTokenAsync(refreshToken);

            var parent = await _context.Parents.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (parent == null)
            {
                parent = new Parent { UserId = user.Id };
                _context.Parents.Add(parent);
                await _context.SaveChangesAsync();
            }

            if (!string.IsNullOrEmpty(tokenData.RefreshToken))
            {
                Response.Cookies.Append("refresh_token", tokenData.RefreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    MaxAge = TimeSpan.FromSeconds(tokenData.RefreshExpiresIn),
                    Path = "/"
                });
            }

            return Ok(new
            {
                accessToken = tokenData.AccessToken,
                expiresIn = tokenData.ExpiresIn,
                profileId = parent.Id
            });
        }

        [Authorize]
        [HttpGet("check-parent")]
        public async Task<IActionResult> CheckParent()
        {
            var user = await GetAuthenticatedUserAsync();
            var hasRecord = user != null && user.Id > 0
                && await _context.Parents.AnyAsync(p => p.UserId == user.Id);
            return Ok(new { HasParentRecord = hasRecord });
        }
    }
}
