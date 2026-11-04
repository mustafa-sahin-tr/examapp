using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Parents;

namespace ExamApp.Api.Services.TopicPerformance;

public interface IParentTopicPerformanceService
{
    /// <returns>Veli bu çocuğa Active bağlı değilse <c>null</c> (çağıran 404'e çevirir).</returns>
    Task<TopicPerformanceDto?> GetAsync(int parentUserId, int studentId, int days, int? subjectId, TopicPerformanceGroupBy groupBy, int minQuestions, CancellationToken ct = default);
}

/// <summary>Issue #498: veli uçlarının sırası: kapı, erişim kaydı, veri. Öğrenci kimliği kapıdan geçen bağlantıdan okunur.</summary>
public sealed class ParentTopicPerformanceService(
    IParentChildAccess access,
    IParentAccessAuditLog audit,
    ITopicPerformanceService performance) : IParentTopicPerformanceService
{
    public async Task<TopicPerformanceDto?> GetAsync(int parentUserId, int studentId, int days, int? subjectId, TopicPerformanceGroupBy groupBy, int minQuestions, CancellationToken ct = default)
    {
        var grant = await access.EnsureActiveChildAsync(parentUserId, studentId, ct);
        if (grant is null)
            return null;

        // Veri okunmadan önce: okuma hata verse de erişim denemesi kayıtta kalır.
        await audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildTopicPerformance, ct: ct);

        // Saate kesilir: sık sorguda sayının değiştiği an çocuğun cevap saatini dakikasıyla ele vermesin. Bedeli: son cevap
        // veliye en çok bir saat geç yansır. Diğer veli uçlarıyla aynı kural (ParentDashboardService.TruncateToHour).
        var now = DateTime.UtcNow;
        var to = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
        return await performance.GetAsync(grant.StudentId, to.AddDays(-days), to, subjectId, groupBy, minQuestions, ct);
    }
}
