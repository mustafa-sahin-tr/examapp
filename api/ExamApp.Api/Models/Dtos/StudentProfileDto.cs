using System;
using System.Linq.Expressions;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class StudentProfileDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public int XP { get; set; }
    public int Level { get; set; } // 🟢 En son seviy
    
    public int TotalQuestionsSolved { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int TestsCompleted { get; set; }
    public int TotalRewards { get; set; }
    public List<StudentBadge> Badges { get; set; } = new List<StudentBadge>();
    public int LeaderboardRank { get; set; }
    public List<StudentWorkSheetSummaryDto> RecentTests { get; set; } = new List<StudentWorkSheetSummaryDto>();

//                     s.User.FullName,
//                     s.User.AvatarUrl,
//                     s.GradeId,
//                     XP = s.StudentPoints.Sum(sp => sp.XP),
//                     Level = s.StudentPoints.OrderByDescending(sp => sp.LastUpdated).Select(sp => sp.Level).FirstOrDefault(), // 🟢 En son seviye
//                     TotalQuestionsSolved = _context.StudentPointHistories.Count(p => p.StudentId == s.Id),
//                     CorrectAnswers = _context.StudentPointHistories.Count(p => p.StudentId == s.Id && p.Reason == "Doğru Cevap"),
//                     WrongAnswers = _context.StudentPointHistories.Count(p => p.StudentId == s.Id && p.Reason == "Yanlış Cevap"),
//                     TestsCompleted = _context.TestInstances.Count(t => t.StudentId == s.Id),
//                     TotalRewards = _context.StudentRewards.Count(r => r.StudentId == s.Id),
//                     Badges = _context.StudentBadges
//                         .Where(sb => sb.StudentId == s.Id)
//                         .Select(sb => new { sb.Badge.Name, sb.Badge.ImageUrl })
//                         .ToList(),
//                     LeaderboardRank = _context.Leaderboards
//                         .Where(lb => lb.StudentId == s.Id)
//                         .OrderByDescending(lb => lb.RecordedAt)
//                         .Select(lb => lb.Rank)
//                         .FirstOrDefault(),
//                     RecentTests = _context.TestInstances
//                         .Where(ti => ti.StudentId == s.Id)
//                         .OrderByDescending(ti => ti.StartTime)
//                         .Take(5)
//                         .Select(ti => new
//                         {
//                             ti.Worksheet.Name,
//                             ti.StartTime,
//                             Score = ti.WorksheetInstanceQuestions.Count(tiq => tiq.IsCorrect) * 10, // 10 puan üzerinden hesaplama
//                             TotalQuestions = ti.WorksheetInstanceQuestions.Count()
//                         })
//                         .ToList()
}

public class StudentWorkSheetSummaryDto {
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public int Score { get; set; } // 10 puan üzerinden hesaplama
    public int TotalQuestions { get; set; }

}

public class StudentDto {
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public int XP { get; set; }
    public int Level { get; set; } // 🟢 En son seviy
    public string SchoolName { get; set; } = string.Empty;
    public string StudentNumber { get; set; } = string.Empty;
}

public class TeacherDto {
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string SchoolName { get; set; } = string.Empty;
}