using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentController : BaseController
    {
        private readonly IMinIoService _minioService;
        private readonly IJwtService _jwtService;

        private readonly IStudentService _studentService;

        private readonly IUserService _userService;

        public StudentController(IMinIoService minioService, IJwtService jwtService, IStudentService studentService, IUserService userService)
            : base()
        {
            _minioService = minioService;
            _jwtService = jwtService;
            _studentService = studentService;
            _userService = userService;
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
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));            

            if (avatar == null || avatar.Length == 0)
                return BadRequest(new { message = "Geçersiz dosya." });

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(avatar.FileName)}";
            var filePath = $"avatars/{fileName}";

            using (var stream = avatar.OpenReadStream())
            {
                await _minioService.UploadFileAsync(stream, filePath, "student-avatars");
            }
            await _userService.UpdateUserAvatar(userId, filePath);
            return await GetStudentProfile();
        }



        [Authorize] // 🔹 Kullanıcının giriş yapmış olması gerekiyor
        [HttpPost("register")]
        public async Task<IActionResult> RegisterStudent(RegisterStudentDto request)
        {
            // 🔹 Token’dan UserId'yi al
            var user = await GetAuthenticatedUserAsync();

            // 🔹 Öğrenci zaten var mı?
            var response = await _studentService.Save(user.Id, request);
            if(response == null)
            {
                return BadRequest(new { message = "Öğrenci kaydı başarısız." });
            }

            if(response.Success == false)
            {
                return BadRequest(new { message = response.Message });
            }

            return Ok(new { Message = "Student registered successfully.", 
                StudentId = response.ObjectId,
                RefreshToken = _jwtService.GenerateToken(user) // Token'ı burada döndürüyoruz
                 });
        }


        [Authorize]
        [HttpGet("check-student")]
        public async Task<IActionResult> CheckStudent()
        {
            var student = await GetAuthenticatedStudentAsync();

            if (student != null)
            {
                return Ok(new { HasStudentRecord = true, Student = student });
            }

            return Ok(new { HasStudentRecord = false });
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
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var student = await _studentService.GetStudentProfile(userId);
            if (student == null)
            {
                return NotFound(new { message = "Öğrenci bulunamadı." });
            }
            return Ok(student);
        }


    }
}
