using System.Text.Json;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// Issue #1265: aktarım akışına olay ekler (<see cref="BookImportJobEvent"/>). Olay bağlama eklenir; çağıranın bir sonraki
/// <c>SaveChangesAsync</c>'i ile yazılır. Kodlar arayüzdeki çeviri anahtarlarıdır (<c>book-imports</c> i18n, <c>activity.*</c>).
/// </summary>
public static class BookImportEvents
{
    public const string Started = "started";
    public const string Resumed = "resumed";
    public const string StageStarted = "stageStarted";
    public const string PagesReady = "pagesReady";
    public const string CleaningUnavailable = "cleaningUnavailable";
    public const string CleaningAnalysisStarted = "cleaningAnalysisStarted";
    public const string CleaningNothingFound = "cleaningNothingFound";
    public const string CleaningAnalysisFinished = "cleaningAnalysisFinished";
    public const string CleaningFinished = "cleaningFinished";
    public const string CleaningFailed = "cleaningFailed";
    public const string SetupFinished = "setupFinished";
    public const string SetupConfirmed = "setupConfirmed";
    public const string TestStarted = "testStarted";
    public const string TestFinished = "testFinished";
    public const string Paused = "paused";
    public const string Retried = "retried";
    public const string Continued = "continued";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string Finished = "finished";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Add(AppDbContext db, BookImportJob job, string code, object? data = null) =>
        db.BookImportJobEvents.Add(new BookImportJobEvent
        {
            JobId = job.Id,
            Stage = job.Stage,
            Code = code,
            DataJson = data is null ? null : JsonSerializer.Serialize(data, Json),
            CreatedAt = DateTime.UtcNow,
        });
}
