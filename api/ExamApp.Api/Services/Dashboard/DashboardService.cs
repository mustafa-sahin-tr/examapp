using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Dashboard;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        // Beş basit COUNT sorgusu. AppDbContext thread-safe olmadığı için art arda çalıştırılır
        // (Task.WhenAll aynı context üzerinde kullanılamaz). Include/navigation yok, N+1 yok.
        var teacherCount = await _context.Teachers.AsNoTracking().CountAsync(ct);
        var studentCount = await _context.Students.AsNoTracking().CountAsync(ct);
        var worksheetCount = await _context.Worksheets.AsNoTracking().CountAsync(ct);
        var questionCount = await _context.Questions.AsNoTracking().CountAsync(ct);
        var aiClassifiedCount = await _context.Questions.AsNoTracking()
            .CountAsync(q => q.ClassificationSource == ClassificationSource.AI, ct);

        return new DashboardSummaryDto
        {
            TeacherCount = teacherCount,
            StudentCount = studentCount,
            WorksheetCount = worksheetCount,
            QuestionCount = questionCount,
            AiClassifiedQuestionCount = aiClassifiedCount,
            AiClassifiedRatio = questionCount == 0 ? 0 : (double)aiClassifiedCount / questionCount
        };
    }
}
