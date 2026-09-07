using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class TeacherService : ITeacherService
{
    private readonly AppDbContext _context;

    public TeacherService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Teacher?> GetTeacher(int userId)
    {
        return await _context.Teachers
            .Where(t => t.UserId == userId)
            .FirstOrDefaultAsync();
    }

    public async Task<ResponseBaseDto> Save(int userId, RegisterTeacherDto dto)
    {
        if (dto.SchoolId.HasValue &&
            !await _context.Schools.AnyAsync(s => s.Id == dto.SchoolId.Value))
        {
            return new ResponseBaseDto
            {
                Success = false,
                Message = "Seçilen okul bulunamadı."
            };
        }

        var existingTeacher = await _context.Teachers.FirstOrDefaultAsync(s => s.UserId == userId);
        if (existingTeacher != null)
        {
            existingTeacher.SchoolId = dto.SchoolId;
            await _context.SaveChangesAsync();
            return new ResponseBaseDto
            {
                Success = true,
                Message = "Öğretmen başarıyla güncellendi..",
                ObjectId = existingTeacher.Id
            };
        }

        // 🔹 Yeni öğrenci kaydını ekle
        var teacher = new Teacher
        {
            UserId = userId,
            SchoolId = dto.SchoolId
        };

        _context.Teachers.Add(teacher);
        await _context.SaveChangesAsync();
        return new ResponseBaseDto
        {
            Success = true,
            Message = "Öğretmen başarıyla kaydedildi.",
            ObjectId = teacher.Id
        };
    }

    public async Task<UpdateThemeDto> UpdateTeacherTheme(int userId, string themePreset, string? themeCustomConfig)
    {
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == userId);
        if (teacher == null)
        {
            return new UpdateThemeDto
            {
                Success = false,
                Message = "Öğretmen bulunamadı."
            };
        }

        teacher.ThemePreset = themePreset;
        teacher.ThemeCustomConfig = themeCustomConfig;

        await _context.SaveChangesAsync();

        return new UpdateThemeDto
        {
            Success = true,
            Message = "Theme tercihi güncellendi.",
            ObjectId = teacher.Id,
            ThemePreset = teacher.ThemePreset,
            ThemeCustomConfig = teacher.ThemeCustomConfig
        };
    }

    public async Task<TeacherDashboardSummaryDto> GetDashboardSummaryAsync(int teacherId, CancellationToken ct = default)
    {
        // Sahiplik: sadece CreateUserId == teacherId olan worksheet'ler; paylaşılanlar hariç.
        // Soft-delete edilmiş kayıtlar AppDbContext global query filter ile zaten dışarıda.
        var ownedWorksheets = _context.Worksheets
            .AsNoTracking()
            .Where(w => w.CreateUserId == teacherId);

        var totalWorksheets = await ownedWorksheets.CountAsync(ct);

        if (totalWorksheets == 0)
        {
            return new TeacherDashboardSummaryDto { TotalWorksheets = 0, TotalUniqueStudents = 0 };
        }

        // Sahip olunan worksheet'lerin atamaları; sadece hedefleme alanları projeksiyonlanır.
        var assignmentTargets = await _context.WorksheetAssignments
            .AsNoTracking()
            .Where(wa => ownedWorksheets.Any(w => w.Id == wa.WorksheetId))
            .Select(wa => new { wa.StudentId, wa.GradeId, wa.SchoolId })
            .ToListAsync(ct);

        if (assignmentTargets.Count == 0)
        {
            return new TeacherDashboardSummaryDto { TotalWorksheets = totalWorksheets, TotalUniqueStudents = 0 };
        }

        // WorksheetAssignmentService ile aynı genişletme mantığı:
        //  - StudentId dolu -> direkt öğrenci
        //  - GradeId dolu, StudentId boş -> o sınıftaki tüm öğrenciler (SchoolId doluysa o okulla sınırlı)
        var directStudentIds = assignmentTargets
            .Where(a => a.StudentId.HasValue)
            .Select(a => a.StudentId!.Value)
            .Distinct()
            .ToList();

        var gradeTargets = assignmentTargets
            .Where(a => a.GradeId.HasValue && !a.StudentId.HasValue)
            .Select(a => new { GradeId = a.GradeId!.Value, a.SchoolId })
            .Distinct()
            .ToList();

        var gradeIds = gradeTargets.Select(g => g.GradeId).Distinct().ToList();

        // Direkt StudentId atamaları da Students tablosu üzerinden doğrulanır ki
        // soft-delete edilmiş öğrenciler sınıf-bazlı yolla tutarlı biçimde dışlansın.
        var existingDirectStudentIds = directStudentIds.Count > 0
            ? await _context.Students
                .AsNoTracking()
                .Where(s => directStudentIds.Contains(s.Id))
                .Select(s => s.Id)
                .ToListAsync(ct)
            : new List<int>();

        var targetStudentIds = new HashSet<int>(existingDirectStudentIds);

        if (gradeIds.Count > 0)
        {
            var gradeStudents = await _context.Students
                .AsNoTracking()
                .Where(s => s.GradeId.HasValue && gradeIds.Contains(s.GradeId.Value))
                .Select(s => new { s.Id, GradeId = s.GradeId!.Value, s.SchoolId })
                .ToListAsync(ct);

            foreach (var student in gradeStudents)
            {
                var matches = gradeTargets.Any(g =>
                    g.GradeId == student.GradeId &&
                    (!g.SchoolId.HasValue || g.SchoolId == student.SchoolId));

                if (matches)
                {
                    targetStudentIds.Add(student.Id);
                }
            }
        }

        return new TeacherDashboardSummaryDto
        {
            TotalWorksheets = totalWorksheets,
            TotalUniqueStudents = targetStudentIds.Count
        };
    }

}
