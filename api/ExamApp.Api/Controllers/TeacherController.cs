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
        private readonly IJwtService _jwtService;

        private readonly ITeacherService _teacherService;

        private readonly IUserService _userService;

        public TeacherController(IJwtService jwtService, ITeacherService teacherService, IUserService userService)
            : base()
        {
            _jwtService = jwtService;
            _teacherService = teacherService;
            _userService = userService;
        }

        [Authorize] // 🔹 Kullanıcının giriş yapmış olması gerekiyor
        [HttpPost("register")]
        public async Task<IActionResult> RegisterTeacher(RegisterTeacherDto request)
        {
            // 🔹 Token’dan UserId'yi al
            var user = await GetAuthenticatedUserAsync();

            // 🔹 Öğrenci zaten var mı?
            var response = await _teacherService.Save(user.Id, request);
            if(response == null)
            {
                return BadRequest(new { message = "Öğretmen kaydı başarısız." });
            }

            if(response.Success == false)
            {
                return BadRequest(new { message = response.Message });
            }

            return Ok(new { Message = "Student registered successfully.", 
                TeacherId = response.ObjectId,
                RefreshToken = _jwtService.GenerateToken(user) // Token'ı burada döndürüyoruz
                 });
        }


        [Authorize]
        [HttpGet("check-teacher")]
        public async Task<IActionResult> CheckTeacher()
        {
            var teacher = await GetAuthenticatedTeacherAsync();

            if (teacher != null)
            {
                return Ok(new { HasTeacherRecord = true, Teacher = teacher });
            }

            return Ok(new { HasTeacherRecord = false });
        }
        
    }
}
