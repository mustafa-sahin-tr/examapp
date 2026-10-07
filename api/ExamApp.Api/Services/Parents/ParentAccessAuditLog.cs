using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Veli erişim kaydı yazıcısı (issue #420). Yalnızca erişim VERİLEN istekler için çağrılır. Review: aynı (veli, öğrenci, uç)
/// için <see cref="ParentAccessAuditLog.DedupWindow"/>'luk sabit kova başına EN FAZLA bir satır — panelin yenilenmesi/çocuk
/// değişimi tabloyu şişirmesin. Kontrol "kovada satır var mı → atla"; eşzamanlı iki ilk istek nadiren iki satır yazabilir
/// (kabul edilen yarış, unique index yok).
/// </summary>
public interface IParentAccessAuditLog
{
    /// <returns>Satır yazıldıysa true; aynı kovada zaten kayıt varsa false.</returns>
    Task<bool> RecordAsync(int parentId, int studentId, string endpoint, CancellationToken ct = default);
}

/// <inheritdoc cref="IParentAccessAuditLog"/>
public sealed class ParentAccessAuditLog : IParentAccessAuditLog
{
    private readonly AppDbContext _context;
    private readonly TimeProvider _time;

    public ParentAccessAuditLog(AppDbContext context, TimeProvider? time = null)
    {
        _context = context;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Tekilleştirme kovası (UTC'de sabit 10 dakikalık dilimler: 12:00-12:10, 12:10-12:20 …).</summary>
    public static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(10);

    /// <summary><paramref name="at"/>'in düştüğü kovanın başlangıcı.</summary>
    internal static DateTime BucketStart(DateTime at)
        => new(at.Ticks - at.Ticks % DedupWindow.Ticks, DateTimeKind.Utc);

    public async Task<bool> RecordAsync(int parentId, int studentId, string endpoint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Parent access audit requires the endpoint name.", nameof(endpoint));

        var now = _time.GetUtcNow().UtcDateTime;
        var bucketStart = BucketStart(now);
        var bucketEnd = bucketStart + DedupWindow;

        // IX_ParentAccessAudits_ParentId_At ile daralır. İptal token'ı kullanılmaz (aşağıdaki yazımla aynı gerekçe).
        var alreadyRecorded = await _context.ParentAccessAudits.AsNoTracking()
            .AnyAsync(a => a.ParentId == parentId && a.At >= bucketStart && a.At < bucketEnd
                && a.StudentId == studentId && a.Endpoint == endpoint, CancellationToken.None);
        if (alreadyRecorded)
            return false;

        _context.ParentAccessAudits.Add(new ParentAccessAudit
        {
            ParentId = parentId,
            StudentId = studentId,
            Endpoint = endpoint,
            At = now
        });

        // Veri dönmeden ÖNCE yazılır (yazılamazsa istek 500 — iz bırakmadan veri dönmez). İstemci bağlantıyı kesse de
        // kayıt tamamlansın diye iptal token'ı kullanılmaz (AdminDataAccessAuditService ile aynı karar).
        await _context.SaveChangesAsync(CancellationToken.None);
        return true;
    }
}
