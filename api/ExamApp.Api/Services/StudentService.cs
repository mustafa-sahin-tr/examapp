using System;
using System.Collections.Generic;
using System.Linq;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class StudentService : IStudentService
{
    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;

    public StudentService(AppDbContext context, IAuthApiClient authApiClient)
    {
        _context = context;
        _authApiClient = authApiClient;
    }
    public async Task<List<Grade>> GetGradesAsync()
    {
        var grades = await _context.Grades.ToListAsync();
        return grades;
    }
    public async Task<StudentProfileDto> GetStudentProfile(int userId)
    {
        var student = await _context.Students
            .Where(s => s.UserId == userId)
            .Select(s => new StudentProfileDto
            {
                Id = s.Id,
                // FullName = s.User.FullName,
                // AvatarUrl = s.User.AvatarUrl,
                GradeId = s.GradeId,
                XP = s.StudentPoints.Sum(sp => sp.XP),
                Level = s.StudentPoints.OrderByDescending(sp => sp.LastUpdated).Select(sp => sp.Level).FirstOrDefault(), // 🟢 En son seviye
                TotalQuestionsSolved = _context.StudentPointHistories.Count(p => p.StudentId == s.Id),
                CorrectAnswers = _context.StudentPointHistories.Count(p => p.StudentId == s.Id && p.Reason == "Doğru Cevap"),
                WrongAnswers = _context.StudentPointHistories.Count(p => p.StudentId == s.Id && p.Reason == "Yanlış Cevap"),
                TestsCompleted = _context.TestInstances.Count(t => t.StudentId == s.Id),
                TotalRewards = _context.StudentRewards.Count(r => r.StudentId == s.Id),
                Badges = _context.StudentBadges
                    .Where(sb => sb.StudentId == s.Id)
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
                    .Select(ti => new StudentWorkSheetSummaryDto
                    {
                        Id = ti.Id,
                        Name = ti.Worksheet.Name,
                        StartTime = ti.StartTime,
                        Score = ti.WorksheetInstanceQuestions.Count(tiq => tiq.IsCorrect) * 10, // 10 puan üzerinden hesaplama
                        TotalQuestions = ti.WorksheetInstanceQuestions.Count()
                    })
                    .ToList()
            })
            .FirstOrDefaultAsync();

        return student;

    }

    public async Task<ResponseBaseDto> Save(int userId, RegisterStudentDto dto)
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == userId);
        if (student != null)
        {
            student.StudentNumber = dto.StudentNumber;
            student.SchoolName = dto.SchoolName;
            student.GradeId = dto.GradeId;
        }
        else
        {
            // 🔹 Yeni öğrenci kaydını ekle
            student = new Student
            {
                UserId = userId,
                StudentNumber = dto.StudentNumber,
                SchoolName = dto.SchoolName,
                GradeId = dto.GradeId
            };
            _context.Students.Add(student);
        }

        await _context.SaveChangesAsync();
        return new ResponseBaseDto
        {
            Success = true,
            Message = "Öğrenci başarıyla kaydedildi.",
            ObjectId = student.Id
        };
    }

    public async Task<ResponseBaseDto> UpdateStudentGrade(int userId, int gradeId)
    {
        var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == userId);
        if (student == null)
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = "Öğrenci bulunamadı."
            };
        }

        student.GradeId = gradeId;
        await _context.SaveChangesAsync();
        return new ResponseBaseDto
        {
            Success = true,
            Message = "Öğrenci sınıfı başarıyla güncellendi."
        };

    }

    // public async Task<Dictionary<DateTime, int>> GetStudentActivityHeatmap(int studentId)
    // {
    //     var activityData = await _context.StudentActivities
    //         .Where(sa => sa.StudentId == studentId)
    //         .GroupBy(sa => sa.ActivityDate.Date)
    //         .Select(g => new
    //         {
    //             Date = g.Key,
    //             ActivityCount = g.Count()
    //         })
    //         .ToDictionaryAsync(x => x.Date, x => x.ActivityCount);

    //     return activityData;
    // }

    public async Task<List<StudentLookupDto>> GetStudentLookupsAsync()
    {
        var students = await _context.Students
            .AsNoTracking()
            .Select(s => new StudentLookupDto
            {
                Id = s.Id,
                UserId = s.UserId,
                StudentNumber = s.StudentNumber,
                SchoolName = s.SchoolName,
                GradeId = s.GradeId
            })
            .OrderBy(s => s.StudentNumber)
            .ThenBy(s => s.Id)
            .ToListAsync();

        if (students.Count == 0)
        {
            return students;
        }

        var userIds = students.Select(s => s.UserId).Distinct().ToList();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds);
            var userLookup = users.ToDictionary(u => u.Id);

            foreach (var student in students)
            {
                if (userLookup.TryGetValue(student.UserId, out var user))
                {
                    student.FullName = user.FullName ?? string.Empty;
                    student.Email = user.Email ?? string.Empty;
                    student.AvatarUrl = user.Avatar ?? string.Empty;
                }
            }
        }
        catch
        {
            // Auth API erişilemezse mevcut listeyi base bilgilerle döndür.
        }

        return students;
    }
}
