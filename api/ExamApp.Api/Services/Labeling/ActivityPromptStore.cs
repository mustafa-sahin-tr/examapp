using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #579/#580: <c>LabelingActivityPromptVersions</c> okuması — bir talimat türünün (yapı tipi ya da çözüm talimatı) etkin sürümü:
/// türün en yüksek sürümü, kayıt yoksa koddaki varsayılan (sürüm 0).
/// </summary>
internal static class ActivityPromptStore
{
    public static async Task<ActivityPromptSettings> GetActiveAsync(AppDbContext db, string kind, CancellationToken ct)
    {
        var latest = await GetActiveVersionAsync(db, kind, ct);
        return latest is null
            ? ActivityStructureSettings.Default(kind)
            : new ActivityPromptSettings(kind, latest.Version, latest.Instruction);
    }

    public static Task<LabelingActivityPromptVersion?> GetActiveVersionAsync(AppDbContext db, string kind, CancellationToken ct) =>
        db.LabelingActivityPromptVersions.AsNoTracking()
            .Where(p => p.InteractionType == kind)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);
}
