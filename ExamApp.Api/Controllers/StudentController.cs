using System.Security.Claims;
using ExamApp.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentController : ControllerBase
    {
        private readonly AppDbContext _context;

        public StudentController(AppDbContext context)
        {
            _context = context;
        }

        [Authorize] // 🔹 Kullanıcının giriş yapmış olması gerekiyor
        [HttpPost("register-student")]
        public async Task<IActionResult> RegisterStudent(RegisterStudentDto request)
        {
            // 🔹 Token’dan UserId'yi al
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.Role != UserRole.Student)
            {
                return BadRequest("Invalid User ID or User is not a Student.");
            }

            // 🔹 Öğrenci zaten var mı?
            var existingStudent = await _context.Students.FirstOrDefaultAsync(s => s.UserId == userId);
            if (existingStudent != null)
            {
                return BadRequest("Student record already exists.");
            }

            // 🔹 Yeni öğrenci kaydını ekle
            var student = new Student
            {
                UserId = userId,
                StudentNumber = request.StudentNumber,
                SchoolName = request.SchoolName
            };

            _context.Students.Add(student);
            await _context.SaveChangesAsync();

            return Ok(new { Message = "Student registered successfully.", StudentId = student.Id });
        }


        [Authorize]
        [HttpGet("check-student")]
        public async Task<IActionResult> CheckStudent()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == userId);

            if (student != null)
            {
                return Ok(new { HasStudentRecord = true, Student = student });
            }

            return Ok(new { HasStudentRecord = false });
        }

    }
}
