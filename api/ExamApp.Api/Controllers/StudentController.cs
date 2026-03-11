using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.StudentReset;
using Hangfire;
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

        private readonly IBackgroundJobClient _backgroundJobs;
        private readonly StudentResetJob _studentResetJob;


        public StudentController(
            IMinIoService minioService,
            IStudentService studentService,
            UserProfileCacheService userProfileCacheService,
            IOptions<KeycloakSettings> options,
            IKeycloakService keycloakService,
            IBackgroundJobClient backgroundJobs,
            StudentResetJob studentResetJob)
            : base()
        {
            _minioService = minioService;
            _studentService = studentService;
            _userProfileCacheService = userProfileCacheService;
            _keycloakService = keycloakService;
            _keycloakSettings = options.Value;
            _backgroundJobs = backgroundJobs;
            _studentResetJob = studentResetJob;
        }

        [Authorize(Roles = "Student")]
        [HttpPost("me/reset")]
        public async Task<IActionResult> ResetMyStudentData(CancellationToken cancellationToken)
        {
            var user = await _userProfileCacheService.GetAsync(KeyCloakId);
            if (user == null)
            {
                return Unauthorized("Kullanıcı kimlik doğrulaması başarısız oldu");
            }

            var student = await _studentService.GetStudentProfile(user.Id);
            if (student == null)
            {
                return NotFound(new { message = "Öğrenci bulunamadı." });
            }

            // Enqueue a Hangfire job so reset is handled asynchronously.
            // We pass userId + studentId + keycloak id, but we do NOT store end-user JWT.
            var jobId = _backgroundJobs.Enqueue(() =>
                _studentResetJob.RunAsync(user.Id, student.Id, KeyCloakId));

            return Accepted(new { jobId, message = "Sıfırlama işlemi kuyruğa alındı." });
        }

        // Handy for manual browser testing; the actual reset must be triggered via POST.
        [Authorize(Roles = "Student")]
        [HttpGet("me/reset")]
        public IActionResult ResetMyStudentDataHelp()
        {
            return Ok(new
            {
                message = "Bu endpoint POST ile çalışır: POST /api/exam/student/me/reset"
            });
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
                expiresIn = tokenData.ExpiresIn,
                profileId = user.Id
            });

        }

        // [Authorize]
        // [HttpGet("activity-heatmap")]
        // public async Task<IActionResult> GetActivityHeatmap()
        // {
        //     var user = await _userProfileCacheService.GetAsync(KeyCloakId);
        //     if (user == null)
        //     {
        //         return NotFound(new { message = "Kullanıcı bulunamadı." });
        //     }
        //     var student = await _studentService.GetStudentProfile(user.Id);
        //     if (student == null)
        //     {
        //         return NotFound(new { message = "Öğrenci bulunamadı." });
        //     }
        //     var activityData = await _studentService.GetStudentActivityHeatmap(student.Id);
        //     return Ok(activityData);
        // }


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

        [Authorize(Roles = "Teacher")]
        [HttpGet("lookup")]
        public async Task<IActionResult> GetStudentLookup()
        {
            var students = await _studentService.GetStudentLookupsAsync();
            return Ok(students);
        }

        [Authorize]
        [HttpPost("update-theme")]
        public async Task<IActionResult> UpdateTheme([FromBody] UpdateThemeDto request)
        {
            var user = await GetAuthenticatedUserAsync();
            var response = await _studentService.UpdateStudentTheme(user.Id, request.ThemePreset, request.ThemeCustomConfig);

            if (response == null || !response.Success)
            {
                return BadRequest(new { message = response?.Message ?? "Theme güncellenirken hata oluştu." });
            }

            return Ok(response);
        }


    }
}
