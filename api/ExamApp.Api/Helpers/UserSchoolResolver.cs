using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #326 (D3): bir kullanıcının okulunun (tenant) TEK tanımı. İki varyant:
/// <list type="bullet">
/// <item>Genel kural (<see cref="ResolveAsync"/>/<see cref="ResolveManyAsync(AppDbContext, IEnumerable{int?}, CancellationToken, IReadOnlyDictionary{int, int?}?)"/>,
/// aşağıda): <c>SchoolContextResolver</c> (dolayısıyla <c>SchoolScope</c>/<c>ISchoolAccessPolicy</c>), yorum servisi
/// (okuyucu okulu, yazar okulu sabitleme, bildirim alıcısı uygunluğu, sahip sabitlemesinin güncel okul koşulu) ve sorumlu
/// öğretmen sahip fallback'i.</item>
/// <item>Yalnız öğretmen satırı (<see cref="ResolveTeacherSchoolsAsync"/>): öğretmenler arası paylaşım (SchoolOnly) tekil
/// kararları — <c>WorksheetSchoolContext.ResolveSchoolContextAsync</c> (detay, atama, authoring/kopya, erişim talebi,
/// <c>ExamService</c> detay). SQL liste filtresiyle (<c>WorksheetAccess</c> SchoolOnly dalı, sahibin <c>Teachers.SchoolId</c>'si)
/// aynı tabloyu okur; öğretmen satırı olmayan (yalnız öğrenci satırı olan) taraf SchoolOnly eşleşmesi alamaz.</item>
/// </list>
/// Çoklu canlı satır davranışı ikisinde de aynı (belirsiz → null).
/// <para>
/// Bu sınıfı KULLANMAYANLAR (yalnız öğretmen satırı, kendi sorgusu): <c>WorksheetSchoolContext.ResolveTeacherRecordAsync</c>
/// (öğretmen-yetkili kararlar kaydın VARLIĞINI da doğrular, #222), <c>ResolveTeacherSchoolIdAsync</c> ve SQL içinde satır
/// başına çalışan filtreler (<c>WorksheetAccess</c> SchoolOnly liste dalı, authoring'deki grant/talep iptali).
/// </para>
/// <para>
/// Kural (<c>SchoolContextResolver</c>'ın #234/#277 kuralı): kullanıcının CANLI (silinmemiş) <c>Teachers</c> satırı varsa okul
/// HER ZAMAN oradan gelir (SchoolId null ise okulsuz/bağımsız → null, öğrenci satırına düşülmez). Canlı öğretmen satırı yoksa
/// canlı <c>Students</c> satırının DOĞRULANMIŞ SchoolId'si (issue #361: <c>SchoolVerifiedAt</c> null ise beklemedeki üyelik →
/// null, okulsuz). İkisi de yoksa null. Öğretmen satırı önce gelir, çünkü öğretmen kendine
/// <c>Students.SchoolId=X</c> yazıp X'in öğrenci verisine erişememeli (#234 security).
/// </para>
/// <para>
/// Çoklu satır: #259'daki filtreli unique index'ler (<c>IX_Teachers_UserId</c>, <c>IX_Students_UserId</c>,
/// <c>WHERE NOT "IsDeleted"</c>) kullanıcı başına en fazla BİR canlı satırı garanti eder ve global <c>!IsDeleted</c> filtresi
/// silinmiş satırları dışarıda bırakır — bu yüzden eski <c>OrderBy(Id).First</c> seçimi gereksiz. Yine de index'in olmadığı
/// bir ortamda iki canlı satır bulunursa hangisinin doğru olduğu bilinemez: sonuç null (okulsuz) — güvenli taraf, çünkü
/// okulsuz okuyucu okul kapsamlı veriyi görmez ve okulsuz sahip sorumlu öğretmen olmaz. Tahmin YOK.
/// </para>
/// <para>Kullanıcı id'si 0/negatif (legacy) → null, sorgu atılmaz.</para>
/// </summary>
public static class UserSchoolResolver
{
    /// <summary>Tek kullanıcının okulu (bkz. sınıf özeti); okulsuz, kayıtsız ya da belirsizse null.</summary>
    public static async Task<int?> ResolveAsync(AppDbContext context, int userId, CancellationToken ct = default)
    {
        var map = await ResolveManyAsync(context, new[] { userId }, ct);
        return map.GetValueOrDefault(userId);
    }

    /// <summary>
    /// Toplu varyant: en fazla iki sorgu (Teachers, sonra öğretmen satırı olmayanlar için Students). Sözlükte her istenen
    /// pozitif id için bir kayıt vardır (okulsuz/kayıtsız/belirsiz → null).
    /// </summary>
    /// <param name="knownStudentSchools">
    /// Çağıranın zaten okuduğu TEKİL canlı Students satırları (user id → DOĞRULANMIŞ SchoolId, issue #361:
    /// <c>Student.VerifiedSchoolId</c> — ham <c>SchoolId</c> verme). Bu kullanıcılar için Students sorgusu atlanır (kural aynı:
    /// öğretmen satırı varsa yine o esas). Yalnız tek canlı satırı doğrulanmış kullanıcıları ver.
    /// </param>
    public static async Task<IReadOnlyDictionary<int, int?>> ResolveManyAsync(
        AppDbContext context, IEnumerable<int?> userIds, CancellationToken ct = default,
        IReadOnlyDictionary<int, int?>? knownStudentSchools = null)
    {
        var detailed = await ResolveManyDetailedAsync(context, userIds, ct, knownStudentSchools);
        return detailed.ToDictionary(kv => kv.Key, kv => kv.Value.SchoolId);
    }

    /// <summary>
    /// issue #334 (security Düşük-1): <see cref="ResolveManyAsync(AppDbContext, IEnumerable{int?}, CancellationToken, IReadOnlyDictionary{int, int?}?)"/>
    /// ile AYNI sorgu ve kural; ek olarak "okulsuz" (null, belirsiz değil) ile "belirsiz" (birden fazla canlı satır → null)
    /// ayırt edilir (<see cref="UserSchool.Ambiguous"/>). Okulsuzluğa hak tanıyan kararlar (ör. bağımsız öğretmen istisnası)
    /// belirsiz sonucu okulsuz saymamalı.
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, UserSchool>> ResolveManyDetailedAsync(
        AppDbContext context, IEnumerable<int?> userIds, CancellationToken ct = default,
        IReadOnlyDictionary<int, int?>? knownStudentSchools = null)
    {
        var ids = userIds.Where(id => id is > 0).Select(id => id!.Value).Distinct().ToList();
        var result = new Dictionary<int, UserSchool>();
        if (ids.Count == 0)
            return result;

        var teacherRows = await context.Teachers
            .AsNoTracking()
            .Where(t => ids.Contains(t.UserId))
            .Select(t => new { t.UserId, t.SchoolId })
            .ToListAsync(ct);
        var teacherSchools = teacherRows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => UniqueOrAmbiguous(g.Select(r => r.SchoolId)));

        var studentSchools = new Dictionary<int, UserSchool>();
        if (knownStudentSchools != null)
        {
            foreach (var (userId, schoolId) in knownStudentSchools)
                studentSchools[userId] = new UserSchool(schoolId, false);
        }

        var withoutTeacherRow = ids.Where(id => !teacherSchools.ContainsKey(id) && !studentSchools.ContainsKey(id)).ToList();
        if (withoutTeacherRow.Count > 0)
        {
            var studentRows = await context.Students
                .AsNoTracking()
                .Where(s => withoutTeacherRow.Contains(s.UserId))
                // issue #361: doğrulanmamış (beklemedeki) öğrenci üyeliği okul kapsamı vermez → okulsuz sayılır.
                .Select(s => new { s.UserId, SchoolId = s.SchoolVerifiedAt != null ? s.SchoolId : null })
                .ToListAsync(ct);
            foreach (var g in studentRows.GroupBy(r => r.UserId))
                studentSchools[g.Key] = UniqueOrAmbiguous(g.Select(r => r.SchoolId));
        }

        foreach (var id in ids)
        {
            result[id] = teacherSchools.TryGetValue(id, out var teacherSchool)
                ? teacherSchool
                : studentSchools.GetValueOrDefault(id);
        }

        return result;
    }

    /// <inheritdoc cref="ResolveManyAsync(AppDbContext, IEnumerable{int?}, CancellationToken)"/>
    public static Task<IReadOnlyDictionary<int, int?>> ResolveManyAsync(
        AppDbContext context, IEnumerable<int> userIds, CancellationToken ct = default) =>
        ResolveManyAsync(context, userIds.Select(id => (int?)id), ct);

    /// <summary>
    /// "Aynı okul" kararı (issue #326 O2): iki taraf da okullu VE eşit. Okulsuz taraf (null) hiçbir zaman eşleşmez —
    /// <c>null == null</c> aynı okul SAYILMAZ (kullanıcı onayı 2026-09-30).
    /// </summary>
    public static bool SameSchool(int? a, int? b) => a.HasValue && b.HasValue && a.Value == b.Value;

    /// <summary>
    /// YALNIZ öğretmen satırından okul (Students'a düşülmez) — öğretmenler arası paylaşım kararları için (SchoolOnly:
    /// <c>WorksheetSchoolContext.ResolveSchoolContextAsync</c>). Liste filtresi (<c>WorksheetAccess</c> SchoolOnly dalı) SQL'de
    /// sahibin <c>Teachers.SchoolId</c>'sine baktığından tekil karar da aynı tabloyu okumalı. Çoklu satır kuralı genel
    /// varyantla aynı: tek canlı satır → onun okulu; satır yok ya da birden fazla canlı satır → null. Tek sorgu.
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, int?>> ResolveTeacherSchoolsAsync(
        AppDbContext context, IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        var result = new Dictionary<int, int?>();
        if (ids.Count == 0)
            return result;

        var rows = await context.Teachers
            .AsNoTracking()
            .Where(t => ids.Contains(t.UserId))
            .Select(t => new { t.UserId, t.SchoolId })
            .ToListAsync(ct);
        var byUser = rows.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => UniqueOrNull(g.Select(r => r.SchoolId)));

        foreach (var id in ids)
            result[id] = byUser.GetValueOrDefault(id);
        return result;
    }

    /// <summary>Tek canlı satır → onun okulu; birden fazla canlı satır (unique index'siz ortam) → belirsiz → null.</summary>
    private static int? UniqueOrNull(IEnumerable<int?> schools) => UniqueOrAmbiguous(schools).SchoolId;

    /// <summary>Tek canlı satır → onun okulu; satır yok → okulsuz; birden fazla canlı satır → belirsiz (okul null).</summary>
    private static UserSchool UniqueOrAmbiguous(IEnumerable<int?> schools)
    {
        using var e = schools.GetEnumerator();
        if (!e.MoveNext())
            return new UserSchool(null, false);
        var first = e.Current;
        return e.MoveNext() ? new UserSchool(null, true) : new UserSchool(first, false);
    }
}

/// <summary>
/// issue #334: <see cref="UserSchoolResolver.ResolveManyDetailedAsync"/> sonucu. <see cref="Ambiguous"/> true ise birden fazla
/// canlı satır bulundu (unique index'siz ortam) ve <see cref="SchoolId"/> null'dır — "okulsuz" DEĞİL, "bilinmiyor".
/// </summary>
public readonly record struct UserSchool(int? SchoolId, bool Ambiguous);
