using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.SolutionRecordings;

public enum SolutionRecordingError
{
    None = 0,
    InvalidAudio,
    InvalidStrokes,
    InvalidDuration,
    StorageFailed,
}

public sealed record SolutionRecordingAudio(Stream Content, string ContentType);

public sealed record SolutionRecordingSaveResult(SolutionRecordingError Error, QuestionSolutionRecordingDto? Recording);

/// <summary>
/// Issue #1226: soru çözüm kaydı. Yetki çağıranda: öğretmen uçları soru sahibini (ya da admini) denetler, öğrenci uçları
/// <see cref="ResolveForStudentAsync"/> ile soruya çevrilir.
/// </summary>
public interface IQuestionSolutionRecordingService
{
    Task<QuestionSolutionRecordingDto?> GetAsync(int questionId, CancellationToken ct);
    Task<SolutionRecordingAudio?> GetAudioAsync(int questionId, CancellationToken ct);
    /// <summary>Kaydı oluşturur ya da değiştirir (soru başına bir kayıt); eski ses silinir.</summary>
    Task<SolutionRecordingSaveResult> SaveAsync(int questionId, int userId, string? strokesJson, int durationMs,
        Stream audio, long audioLength, string? audioContentType, CancellationToken ct);
    /// <summary>Kayıt yoksa false. Ses de depodan silinir.</summary>
    Task<bool> DeleteAsync(int questionId, CancellationToken ct);
    /// <summary>
    /// Öğrencinin test oturumu satırından (<c>TestInstanceQuestion.Id</c>) soru id'si; satır öğrencinin değilse ya da çözüm
    /// henüz görünmüyorsa null. Kural etkileşim çözümüyle aynı (#570): bitmiş oturum ya da örnek soru.
    /// </summary>
    Task<int?> ResolveForStudentAsync(int studentUserId, int instanceQuestionId, CancellationToken ct);
    /// <summary>Verilen sorulardan çözüm kaydı olanlar.</summary>
    Task<HashSet<int>> QuestionIdsWithRecordingAsync(IReadOnlyCollection<int> questionIds, CancellationToken ct);
}

public sealed class QuestionSolutionRecordingService(
    AppDbContext db,
    IMinIoService minio,
    ILogger<QuestionSolutionRecordingService> logger) : IQuestionSolutionRecordingService
{
    public const int MaxDurationMs = 5 * 60 * 1000;
    /// <summary>İstemci saati ile ses süresi arasındaki küçük fark için pay.</summary>
    public const int DurationSlackMs = 2000;
    public const long MaxAudioBytes = 12 * 1024 * 1024;
    private const string ObjectPrefix = "solution-audio";

    public async Task<QuestionSolutionRecordingDto?> GetAsync(int questionId, CancellationToken ct)
    {
        var row = await db.QuestionSolutionRecordings.AsNoTracking().FirstOrDefaultAsync(r => r.QuestionId == questionId, ct);
        return row is null ? null : ToDto(row);
    }

    public async Task<SolutionRecordingAudio?> GetAudioAsync(int questionId, CancellationToken ct)
    {
        var row = await db.QuestionSolutionRecordings.AsNoTracking()
            .Where(r => r.QuestionId == questionId)
            .Select(r => new { r.AudioObjectPath, r.AudioContentType })
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return null;
        var stream = await minio.GetFileStreamAsync(row.AudioObjectPath);
        return stream is null ? null : new SolutionRecordingAudio(stream, row.AudioContentType);
    }

    public async Task<SolutionRecordingSaveResult> SaveAsync(int questionId, int userId, string? strokesJson, int durationMs,
        Stream audio, long audioLength, string? audioContentType, CancellationToken ct)
    {
        if (durationMs is < 1 or > MaxDurationMs + DurationSlackMs)
            return Failed(SolutionRecordingError.InvalidDuration);
        var strokes = SolutionStrokeDocument.Normalize(strokesJson, durationMs);
        if (strokes is null)
            return Failed(SolutionRecordingError.InvalidStrokes);
        if (audioLength is < 1 or > MaxAudioBytes)
            return Failed(SolutionRecordingError.InvalidAudio);

        using var buffer = new MemoryStream();
        await audio.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var format = SolutionAudioFormat.Detect(bytes, audioContentType);
        if (format is null || bytes.LongLength > MaxAudioBytes)
            return Failed(SolutionRecordingError.InvalidAudio);

        string objectPath;
        try
        {
            using var upload = new MemoryStream(bytes, writable: false);
            objectPath = await minio.UploadFileAsync(upload, $"{ObjectPrefix}/{questionId}/{Guid.NewGuid():N}.{format.Extension}",
                contentType: format.ContentType);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Solution recording audio upload failed for question {QuestionId}", questionId);
            return Failed(SolutionRecordingError.StorageFailed);
        }

        var row = await db.QuestionSolutionRecordings.AsTracking().FirstOrDefaultAsync(r => r.QuestionId == questionId, ct);
        var previousAudio = row?.AudioObjectPath;
        if (row is null)
        {
            row = new QuestionSolutionRecording { QuestionId = questionId, CreatedAt = DateTime.UtcNow };
            db.QuestionSolutionRecordings.Add(row);
        }
        row.RecordedByUserId = userId;
        row.StrokesJson = strokes;
        row.AudioObjectPath = objectPath;
        row.AudioContentType = format.ContentType;
        row.AudioSizeBytes = bytes.LongLength;
        row.DurationMs = durationMs;
        row.SchemaRevision = QuestionSolutionRecording.CurrentSchemaRevision;
        row.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await DeleteAudioAsync(objectPath);
            throw;
        }
        if (!string.IsNullOrEmpty(previousAudio))
            await DeleteAudioAsync(previousAudio);
        return new SolutionRecordingSaveResult(SolutionRecordingError.None, ToDto(row));
    }

    public async Task<bool> DeleteAsync(int questionId, CancellationToken ct)
    {
        var row = await db.QuestionSolutionRecordings.AsTracking().FirstOrDefaultAsync(r => r.QuestionId == questionId, ct);
        if (row is null)
            return false;
        db.QuestionSolutionRecordings.Remove(row);
        await db.SaveChangesAsync(ct);
        await DeleteAudioAsync(row.AudioObjectPath);
        return true;
    }

    public async Task<int?> ResolveForStudentAsync(int studentUserId, int instanceQuestionId, CancellationToken ct)
    {
        var row = await db.TestInstanceQuestions.AsNoTracking()
            .Where(q => q.Id == instanceQuestionId && q.WorksheetInstance.Student.UserId == studentUserId)
            .Select(q => new
            {
                q.WorksheetQuestion.QuestionId,
                q.WorksheetQuestion.Question.IsExample,
                q.WorksheetInstance.Status,
                q.WorksheetInstance.StartTime,
                q.WorksheetInstance.MaxDurationSeconds,
            })
            .FirstOrDefaultAsync(ct);
        if (row is null)
            return null;
        var status = TestTimeLimit.EffectiveStatus(row.Status, row.StartTime, row.MaxDurationSeconds, DateTime.UtcNow);
        return row.IsExample || WorksheetInstanceStatusRules.IsFinished(status) ? row.QuestionId : null;
    }

    public async Task<HashSet<int>> QuestionIdsWithRecordingAsync(IReadOnlyCollection<int> questionIds, CancellationToken ct)
    {
        if (questionIds.Count == 0)
            return [];
        var ids = await db.QuestionSolutionRecordings.AsNoTracking()
            .Where(r => questionIds.Contains(r.QuestionId))
            .Select(r => r.QuestionId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    private async Task DeleteAudioAsync(string objectPath)
    {
        try
        {
            await minio.DeleteFileByUrlAsync(objectPath);
        }
        catch (Exception ex)
        {
            // Sahipsiz nesne kalır; kayıt akışını bozmaz.
            logger.LogWarning(ex, "Solution recording audio could not be deleted: {ObjectPath}", objectPath);
        }
    }

    private static SolutionRecordingSaveResult Failed(SolutionRecordingError error) => new(error, null);

    private static QuestionSolutionRecordingDto ToDto(QuestionSolutionRecording row)
    {
        using var document = JsonDocument.Parse(row.StrokesJson);
        return new QuestionSolutionRecordingDto
        {
            QuestionId = row.QuestionId,
            DurationMs = row.DurationMs,
            AudioContentType = row.AudioContentType,
            AudioSizeBytes = row.AudioSizeBytes,
            SchemaRevision = row.SchemaRevision,
            Strokes = document.RootElement.Clone(),
            UpdatedAt = row.UpdatedAt,
        };
    }
}

/// <summary>
/// Issue #1226: kabul edilen ses kapları. Tür istemcinin bildirdiğine değil dosyanın ilk baytlarına göre belirlenir;
/// tarayıcılar MediaRecorder ile WebM/Opus (Chrome, Firefox) ya da MP4/AAC (Safari, yeni Chrome) üretir.
/// </summary>
public sealed record SolutionAudioFormat(string ContentType, string Extension)
{
    public static SolutionAudioFormat? Detect(ReadOnlySpan<byte> bytes, string? declaredContentType)
    {
        if (bytes.Length < 12)
            return null;
        var declared = (declaredContentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        if (declared.Length > 0 && !declared.StartsWith("audio/", StringComparison.Ordinal)
            && declared is not ("video/webm" or "video/mp4" or "application/octet-stream"))
            return null;
        if (bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
            return new SolutionAudioFormat("audio/webm", "webm");
        if (bytes[4] == (byte)'f' && bytes[5] == (byte)'t' && bytes[6] == (byte)'y' && bytes[7] == (byte)'p')
            return new SolutionAudioFormat("audio/mp4", "m4a");
        if (bytes[0] == (byte)'O' && bytes[1] == (byte)'g' && bytes[2] == (byte)'g' && bytes[3] == (byte)'S')
            return new SolutionAudioFormat("audio/ogg", "ogg");
        if ((bytes[0] == (byte)'I' && bytes[1] == (byte)'D' && bytes[2] == (byte)'3') || (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0))
            return new SolutionAudioFormat("audio/mpeg", "mp3");
        return null;
    }
}
