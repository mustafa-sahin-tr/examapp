using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentController : BaseController
    {
        private readonly IMinIoService _minioService;

        private readonly IStudentService _studentService;

        private readonly IKeycloakService _keycloakService;


        private readonly KeycloakSettings _keycloakSettings;

        private readonly UserProfileCacheService _userProfileCacheService;


        public StudentController(IMinIoService minioService, IStudentService studentService,
             UserProfileCacheService userProfileCacheService,
            IOptions<KeycloakSettings> options, IKeycloakService keycloakService)
            : base()
        {
            _minioService = minioService;
            _studentService = studentService;
            _userProfileCacheService = userProfileCacheService;
            _keycloakService = keycloakService;
            _keycloakSettings = options.Value;
        }

        [HttpPost("update-grade")]
        public async Task<IActionResult> UpdateStudentGrade([FromBody] int newGradeId)
        {
            var user = await GetAuthenticatedUserAsync();
            var response = await _studentService.UpdateStudentGrade(user.Id, newGradeId);
            if (response == null)
            {
                return BadRequest(new { message = "Öğrenci kaydı başarısız." });
            }
            if (response.Success == false)
            {
                return BadRequest(new { message = response.Message });
            }
            return await GetStudentProfile();
        }

        [HttpPost("update-avatar")]
        public async Task<IActionResult> UpdateAvatar(IFormFile avatar)
        {
            var user = await GetAuthenticatedUserAsync();

            if (avatar == null || avatar.Length == 0)
                return BadRequest(new { message = "Geçersiz dosya." });

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(avatar.FileName)}";
            var filePath = $"avatars/{fileName}";

            using (var stream = avatar.OpenReadStream())
            {
                await _minioService.UploadFileAsync(stream, filePath, "student-avatars");
            }
            return await GetStudentProfile();
        }



        [Authorize] // 🔹 Kullanıcının giriş yapmış olması gerekiyor
        [HttpPost("register")]
        public async Task<IActionResult> RegisterStudent(RegisterStudentDto request)
        {
            // 🔹 Token’dan UserId'yi al // token var valid ama user
            var user = await GetAuthenticatedUserAsync();

            await _keycloakService.SetRoleAsync(user.KeycloakId, UserRole.Student);

            var refreshToken = Request.Cookies["refresh_token"];
            if (string.IsNullOrWhiteSpace(refreshToken))
                return Unauthorized("No refresh token provided.");

            // 2. Keycloak token endpoint'ine isteği hazırla
            var tokenData = await _keycloakService.RefreshTokenAsync(refreshToken);

            // 🔹 Öğrenci zaten var mı?
            var response = await _studentService.Save(user.Id, request);
            
            if (response == null)
            {
                return BadRequest(new { message = "Öğrenci kaydı başarısız." });
            }

            if (response.Success == false)
            {
                return BadRequest(new { message = response.Message });
            }
            // _userProfileCacheService.SetAsync(user.KeycloakId, user);
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
            // 4. Access token'ı UI’a dön
            return Ok(new
            {
                accessToken = tokenData.AccessToken,
                expiresIn = tokenData.ExpiresIn
            });

        }


        [Authorize]
        [HttpGet("check-student")]
        public async Task<IActionResult> CheckStudent()
        {
            var user = await _userProfileCacheService.GetAsync(KeyCloakId);
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }
            var student = await _studentService.GetStudentProfile(user.Id);
            if (student != null)
            {
                return Ok(new { HasStudentRecord = true, Student = student });
            }

            return Ok(new { HasStudentRecord = false });
        }


        [HttpGet("grades")]
        public async Task<IActionResult> GetGradesAsync()
        {
            var grades = await _studentService.GetGradesAsync();
            return Ok(grades);
        }

        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetStudentProfile()
        {
            var user = await GetAuthenticatedUserAsync();
            var student = await _studentService.GetStudentProfile(user.Id);
            if (student == null)
            {
                return NotFound(new { message = "Öğrenci bulunamadı." });
            }
            return Ok(student);
        }


    }
}
