using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TeacherController : BaseController
    {

        private readonly ITeacherService _teacherService;

        private readonly UserProfileCacheService _userProfileCacheService;

        public TeacherController(ITeacherService teacherService, UserProfileCacheService userProfileCacheService)
            : base()
        {
            _userProfileCacheService = userProfileCacheService;
            _teacherService = teacherService;
        }

        [Authorize] // 🔹 Kullanıcının giriş yapmış olması gerekiyor
        [HttpPost("register")]
        public async Task<IActionResult> RegisterTeacher(RegisterTeacherDto request)
        {
            // 🔹 Token’dan UserId'yi al
            var user = await GetAuthenticatedUserAsync();

            // 🔹 Öğrenci zaten var mı?
            var response = await _teacherService.Save(user.Id, request);
            if (response == null)
            {
                return BadRequest(new { message = "Öğretmen kaydı başarısız." });
            }

            if (response.Success == false)
            {
                return BadRequest(new { message = response.Message });
            }

            return Ok(new
            {
                Message = "Teacher registered successfully.",
                TeacherId = response.ObjectId,

            });
        }


        [Authorize]
        [HttpGet("check-teacher")]
        public async Task<IActionResult> CheckTeacher()
        {
            var user = await _userProfileCacheService.GetAsync(KeyCloakId);
            if (user == null)
            {
                return NotFound(new { message = "Kullanıcı bulunamadı." });
            }

            var teacher = await _teacherService.GetTeacher(user.Id);

            if (teacher != null)
            {
                return Ok(new { HasTeacherRecord = true, Teacher = teacher });
            }

            return Ok(new { HasTeacherRecord = false });
        }

        [Authorize]
        [HttpPost("update-theme")]
        public async Task<IActionResult> UpdateTheme([FromBody] UpdateThemeDto request)
        {
            var user = await GetAuthenticatedUserAsync();
            var response = await _teacherService.UpdateTeacherTheme(user.Id, request.ThemePreset, request.ThemeCustomConfig);

            if (response == null || !response.Success)
            {
                return BadRequest(new { message = response?.Message ?? "Theme güncellenirken hata oluştu." });
            }

            return Ok(response);
        }

    }
}
