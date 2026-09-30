using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #191: <see cref="WorksheetAccess"/> kural motoruna verilen "sahibin okulu / istekçinin okulu"
/// girdilerinin DB'den (<c>Teachers.SchoolId</c>) çözülmesi. Kural sınıfı DB'siz/saf kalsın diye ayrı;
/// okul değerleri her zaman sunucu tarafında çözülür, client'tan gelen değere güvenilmez.
/// </summary>
public static class WorksheetSchoolContext
{
    /// <summary>
    /// Kullanıcının ÖĞRETMEN kaydındaki okulu — yalnız <c>Teachers.SchoolId</c>; öğretmen kaydı yoksa null (okulsuz sayılır),
    /// Students satırına düşülmez. Bilinçli: çağıranlar (liste SchoolOnly dalı, authoring grant iptali) SQL tarafında sahibin
    /// <c>Teachers.SchoolId</c>'siyle karşılaştırır; iki taraf aynı tablodan okunmalı. Genel kullanıcı okulu için
    /// <see cref="UserSchoolResolver"/> (issue #326 D3).
    /// </summary>
    public static Task<int?> ResolveTeacherSchoolIdAsync(this AppDbContext context, int userId, CancellationToken ct = default)
    {
        return context.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => t.SchoolId)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// issue #222 (security Ö2): istek sahibinin ÖĞRETMEN kaydı ve okulu. BİLİNÇLİ OLARAK yalnız öğretmen satırına bakar,
    /// <see cref="UserSchoolResolver"/>'ı KULLANMAZ: <c>GetSchoolScopeAsync</c> (UserSchoolResolver kuralı) öğretmen satırı
    /// olmayan çok rollü hesapta okulu Students tablosundan çözebilir; öğretmen-yetkili kararlar ise hem kaydın VARLIĞINI
    /// (<c>Exists</c>) hem okulunu öğretmen kaydından doğrulamalıdır. Kayıt yoksa <c>Exists=false</c>. Canlı satır #259 unique
    /// index'iyle tektir; <c>OrderBy(Id)</c> index'siz ortam için deterministik sıra bırakır.
    /// </summary>
    public static async Task<(bool Exists, int? SchoolId)> ResolveTeacherRecordAsync(
        this AppDbContext context, int userId, CancellationToken ct = default)
    {
        var row = await context.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Id)
            .Select(t => new { t.SchoolId })
            .FirstOrDefaultAsync(ct);

        return row == null ? (false, null) : (true, row.SchoolId);
    }

    /// <summary>
    /// Tekil worksheet kararları (detay/atama/kopya/düzenleme/erişim talebi) için (sahibin okulu, istekçinin okulu) çifti.
    /// Yalnızca karar için gerekliyse sorgu atar: worksheet SchoolOnly değilse, istekçi admin veya sahibi
    /// ise DB'ye gitmeden <c>(null, null)</c> döner (bu durumlarda CanView/CanAssign zaten okula bakmaz).
    /// issue #326 (D3): okullar YALNIZ öğretmen satırlarından (<see cref="UserSchoolResolver.ResolveTeacherSchoolsAsync"/>, tek
    /// sorgu) — SchoolOnly bir öğretmen paylaşım kuralı; liste filtresiyle aynı tabloyu okur. Öğretmen satırı olmayan taraf
    /// (Students satırı olsa da) okulsuz sayılır; çoklu canlı satır → null (okulsuz).
    /// </summary>
    public static async Task<(int? OwnerSchoolId, int? RequesterSchoolId)> ResolveSchoolContextAsync(
        this AppDbContext context, Worksheet worksheet, int userId, bool isAdmin, CancellationToken ct = default)
    {
        if (worksheet.TeacherSharing != WorksheetTeacherSharing.SchoolOnly || isAdmin)
            return (null, null);

        var ownerUserId = worksheet.CreateUserId;
        if (!ownerUserId.HasValue || ownerUserId.Value <= 0 || ownerUserId.Value == userId)
            return (null, null);

        var schools = await UserSchoolResolver.ResolveTeacherSchoolsAsync(context, new[] { ownerUserId.Value, userId }, ct);
        return (schools.GetValueOrDefault(ownerUserId.Value), schools.GetValueOrDefault(userId));
    }
}
