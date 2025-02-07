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
        private readonly IMinIoService _minioService;

        public StudentController(AppDbContext context, IMinIoService minioService)
        {
            _context = context;
            _minioService = minioService;
        }

        [HttpPost("update-grade")]
        public async Task<IActionResult> UpdateStudentGrade([FromBody] int newGradeId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.Role != UserRole.Student)
            {
                return BadRequest("Invalid User ID or User is not a Student.");
            }
            var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == userId);
            if (student == null)
                return NotFound(new { message = "Öğrenci bulunamadı." });

            student.GradeId = newGradeId;
            await _context.SaveChangesAsync();

            return await GetStudentProfile();
        }

        [HttpPost("update-avatar")]
        public async Task<IActionResult> UpdateAvatar(IFormFile avatar)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var user = await _context.Users.FindAsync(userId);
            if (user == null || user.Role != UserRole.Student)
            {
                return BadRequest("Invalid User ID or User is not a Student.");
            }

            if (avatar == null || avatar.Length == 0)
                return BadRequest(new { message = "Geçersiz dosya." });

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(avatar.FileName)}";
            var filePath = $"avatars/{fileName}";

            using (var stream = avatar.OpenReadStream())
            {
                await _minioService.UploadFileAsync(stream, filePath, "student-avatars");
            }

            user.AvatarUrl = $"http://localhost/minio-api/student-avatars/{filePath}";
            await _context.SaveChangesAsync();

            return await GetStudentProfile();
            
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

        [HttpGet("grades")]
        public async Task<IActionResult> GetGradesAsync()
        {
            var grades = await _context.Grades.ToListAsync();
            return Ok(grades);
        }

        [Authorize]
        [HttpGet("profile")]
        public async Task<IActionResult> GetStudentProfile()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var student = await _context.Students
                .Include(s => s.User)
                .Where(s => s.UserId == userId)
                .Select(s => new
                {
                    s.Id,
                    s.User.FullName,
                    s.User.AvatarUrl,
                    s.GradeId,
                    XP = s.StudentPoints.Sum(sp => sp.XP),
                    Level = s.StudentPoints.OrderByDescending(sp => sp.LastUpdated).Select(sp => sp.Level).FirstOrDefault(), // 🟢 En son seviye
                    TotalQuestionsSolved = _context.StudentPointHistories.Count(p => p.StudentId == s.Id),
                    CorrectAnswers = _context.StudentPointHistories.Count(p => p.StudentId == s.Id && p.Reason == "Doğru Cevap"),
                    WrongAnswers = _context.StudentPointHistories.Count(p => p.StudentId == s.Id && p.Reason == "Yanlış Cevap"),
                    TestsCompleted = _context.TestInstances.Count(t => t.StudentId == s.Id),
                    TotalRewards = _context.StudentRewards.Count(r => r.StudentId == s.Id),
                    Badges = _context.StudentBadges
                        .Where(sb => sb.StudentId == s.Id)
                        .Select(sb => new { sb.Badge.Name, sb.Badge.ImageUrl })
                        .ToList(),
                    LeaderboardRank = _context.Leaderboards
                        .Where(lb => lb.StudentId == s.Id)
                        .OrderByDescending(lb => lb.RecordedAt)
                        .Select(lb => lb.Rank)
                        .FirstOrDefault(),
                    RecentTests = _context.TestInstances
                        .Where(ti => ti.StudentId == s.Id)
                        .OrderByDescending(ti => ti.StartTime)
                        .Take(5)
                        .Select(ti => new
                        {
                            ti.Test.Name,
                            ti.StartTime,
                            Score = ti.TestInstanceQuestions.Count(tiq => tiq.IsCorrect) * 10, // 10 puan üzerinden hesaplama
                            TotalQuestions = ti.TestInstanceQuestions.Count()
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (student == null)
                return NotFound(new { message = "Öğrenci bulunamadı." });

            return Ok(student);
        }


    }
}
