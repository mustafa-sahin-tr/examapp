using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentController : BaseController
    {
        private readonly IMinIoService _minioService;

        private readonly IStudentService _studentService;

        private readonly IUserService _userService;

        private readonly UserProfileCacheService _userProfileCacheService;


        public StudentController(IMinIoService minioService,IStudentService studentService,
             IUserService userService,  UserProfileCacheService userProfileCacheService)
            : base()
        {
            _minioService = minioService;
            _studentService = studentService;
            _userService = userService;
            _userProfileCacheService = userProfileCacheService;
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
            await _userService.UpdateUserAvatar(user.Id, filePath);
            return await GetStudentProfile();
        }



        [Authorize] // 🔹 Kullanıcının giriş yapmış olması gerekiyor
        [HttpPost("register")]
        public async Task<IActionResult> RegisterStudent(RegisterStudentDto request)
        {                    
            // 🔹 Token’dan UserId'yi al // token var valid ama user
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
                 });
        }


        [Authorize]
        [HttpGet("check-student")]
        public async Task<IActionResult> CheckStudent()
        {
            var user = await _userProfileCacheService.GetAsync(KeyCloakId);
            if(user == null) 
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
