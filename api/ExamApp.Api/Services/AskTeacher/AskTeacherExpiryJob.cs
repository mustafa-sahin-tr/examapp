using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.AskTeacher;

public interface IAskTeacherExpiryJob
{
    Task ExpireAsync(CancellationToken ct);
}

/// <summary>Issue #1111: süresi dolan cevapsız soruları kapatır; hak düşmediği için öğrencinin hakkı geri gelmiş olur.</summary>
public sealed class AskTeacherExpiryJob(IAskTeacherService service, ILogger<AskTeacherExpiryJob> logger) : IAskTeacherExpiryJob
{
    public const string RecurringJobId = "ask-teacher-expiry";

    public async Task ExpireAsync(CancellationToken ct)
    {
        var expired = await service.ExpireAsync(ct);
        if (expired > 0)
            logger.LogInformation("Ask teacher: {Count} unanswered questions expired", expired);
    }
}
