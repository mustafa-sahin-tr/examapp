using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentDashboard;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// Velinin gördüğü atama kümesi ve kova kuralı — V2 özet sayıları (#420, <see cref="ParentDashboardService"/>) ve V3 ödev/test
/// listesi (#421, <see cref="ParentAssignmentService"/>) AYNI yerden okur; listedeki filtre çipleri ile özet kartındaki sayılar
/// birbirinden ayrışmaz.
/// <list type="bullet">
/// <item>Görünürlük: öğrencinin kendi ekranıyla aynı — <see cref="WorksheetStudentAccess.AssignmentVisibleTo"/> (doğrulanmış okul,
/// #361); geri çekilen (soft-delete) atama ve emekliye ayrılan (soft-delete) worksheet düşer.</item>
/// <item>Kapsam: başlamış atamalar — açık olanlar + teslim tarihi son <see cref="WindowDays"/> gün içinde geçmiş olanlar.</item>
/// <item>İlgili instance <see cref="AssignmentInstanceWindow"/>; durum <see cref="AssignmentStudentStatusRules"/> (öğretmen ilerleme
/// ekranıyla aynı); veli kovası <see cref="ToBucket"/>.</item>
/// <item>Worksheet başına TEK sonuç (aynı worksheet hem doğrudan hem sınıfa atanmış olabilir).</item>
/// </list>
/// <para>
/// Kova ile sonuç arasındaki fark (review m2): kova öğretmen durum kuralından (<see cref="AssignmentStudentStatusRules"/>) gelir;
/// o kural legacy "Started + EndTime dolu" oturumu da Completed sayar (eski kayıtlarda test bitince Status güncellenmezdi).
/// Sonuç (<see cref="InstanceRow.HasResult"/>) ise öğrencinin kendi ekranındaki gibi yalnızca bitmiş (Completed/Expired)
/// oturumda açılır — öğrenci o legacy oturumu kendi geçmişinde de sonuç olarak göremez, veli daha fazlasını görmez. Bu yüzden
/// nadiren "tamamlandı" kovasında sonucu olmayan satır olabilir. Tekrar çözüm yoktur (#367): öğrenci başına worksheet başına
/// tek canlı oturum; bir atama penceresinin "ilgili" oturumu <see cref="AssignmentInstanceWindow"/> ile seçilir ve test sonucu
/// ucu yalnızca bu (listede görünen) oturumu açar. Atamadan ÖNCE kendi başına çözülmüş (bitmiş) test, atama var olduğunda o
/// atamayı karşılar ve sonucu açılır (#367 — öğretmen ilerleme ekranı ve liste ile tutarlı); hiç atanmamış test açılmaz.
/// </para>
/// </summary>
internal static class ParentAssignmentScope
{
    /// <summary>Teslim tarihi geçmiş atamalar bu kadar gün geriye bakılarak alınır (eski gecikmeler sonsuza kadar birikmesin).</summary>
    internal const int WindowDays = 30;

    /// <summary>Okunan en fazla atama satırı (güvenlik tavanı; gerçekçi bir öğrencide ulaşılmaz).</summary>
    internal const int MaxRows = 500;

    internal sealed record AssignmentRow(int AssignmentId, int WorksheetId, DateTime StartAt, DateTime? EndAt, int? CreateUserId);

    internal sealed record InstanceRow(int InstanceId, int WorksheetId, DateTime StartTime, WorksheetInstanceStatus Status, DateTime? EndTime)
    {
        /// <summary>
        /// Sonucu var mı: öğrencinin kendi geçmişi/sonuç ekranıyla AYNI kural — <see cref="WorksheetInstanceStatusRules.IsFinished(WorksheetInstanceStatus)"/>
        /// (Completed ya da süresi dolmuş Expired, #396). Devam eden oturumun sonucu yoktur.
        /// </summary>
        public bool HasResult => WorksheetInstanceStatusRules.IsFinished(Status);
    }

    /// <summary>Worksheet başına seçilen temsilci atama + o pencerenin ilgili instance'ı + kova.</summary>
    internal sealed record Classified(AssignmentRow Assignment, InstanceRow? Instance, ParentAssignmentBucket Bucket);

    internal sealed record LoadResult(IReadOnlyList<Classified> Items, bool Capped);

    /// <summary>
    /// Kapsamdaki atamaları ve öğrencinin ilgili instance'larını okur, worksheet başına sınıflandırır. Yalnızca kapıdan
    /// (<see cref="IParentChildAccess"/>) geçmiş bir <paramref name="grant"/> ile çağrılır.
    /// </summary>
    internal static async Task<LoadResult> LoadAsync(AppDbContext context, ParentChildAccessGrant grant, DateTime now, CancellationToken ct)
    {
        var lookbackStart = now.AddDays(-WindowDays);

        var assignments = await context.WorksheetAssignments.AsNoTracking()
            .Where(WorksheetStudentAccess.AssignmentVisibleTo(grant.StudentId, grant.GradeId, grant.VerifiedSchoolId))
            .Where(a => a.StartAt <= now && (a.EndAt == null || a.EndAt >= lookbackStart))
            .Where(a => !a.Worksheet.IsDeleted)
            .OrderByDescending(a => a.StartAt).ThenByDescending(a => a.Id)
            .Take(MaxRows)
            .Select(a => new AssignmentRow(a.Id, a.WorksheetId, a.StartAt, a.EndAt, a.CreateUserId))
            .ToListAsync(ct);

        if (assignments.Count == 0)
            return new LoadResult(Array.Empty<Classified>(), false);

        var worksheetIds = assignments.Select(a => a.WorksheetId).Distinct().ToList();
        var instances = await context.TestInstances.AsNoTracking()
            .Where(ti => ti.StudentId == grant.StudentId && worksheetIds.Contains(ti.WorksheetId))
            .Select(ti => new InstanceRow(ti.Id, ti.WorksheetId, ti.StartTime, ti.Status, ti.EndTime))
            .ToListAsync(ct);

        return new LoadResult(Classify(assignments, instances, now).ToList(), assignments.Count >= MaxRows);
    }

    /// <summary>
    /// Worksheet başına TEK sonuç: atamalardan biri tamamlandıysa Completed; değilse hâlâ yapılabilir bir atama varsa Pending;
    /// aksi halde Overdue. Temsilci atama en büyük kovayı veren; eşitlikte teslim tarihi en geç olan (açık uçlu en geç sayılır),
    /// sonra en yeni başlangıç, sonra en büyük id.
    /// </summary>
    internal static IEnumerable<Classified> Classify(
        IReadOnlyList<AssignmentRow> assignments, IReadOnlyList<InstanceRow> instances, DateTime now)
    {
        var instancesByWorksheet = instances.ToLookup(i => i.WorksheetId);
        return assignments
            .GroupBy(a => a.WorksheetId)
            .Select(g => g
                .Select(a =>
                {
                    var relevant = AssignmentInstanceWindow.SelectRelevant(
                        instancesByWorksheet[a.WorksheetId], a.StartAt, a.EndAt, i => i.StartTime, i => i.Status);
                    var status = AssignmentStudentStatusRules.Resolve(a.StartAt, a.EndAt, relevant?.Status, relevant?.EndTime, now);
                    return new Classified(a, relevant, ToBucket(status, a.EndAt, now));
                })
                .OrderByDescending(c => c.Bucket)
                .ThenByDescending(c => c.Assignment.EndAt ?? DateTime.MaxValue)
                .ThenByDescending(c => c.Assignment.StartAt)
                .ThenByDescending(c => c.Assignment.AssignmentId)
                .First());
    }

    /// <summary>Kova sayıları (worksheet başına tek sayım) — V2 özet kartı ve V3 filtre çipleri.</summary>
    internal static ParentChildAssignmentCountsDto Count(IEnumerable<Classified> items)
    {
        var counts = new ParentChildAssignmentCountsDto { WindowDays = WindowDays };
        foreach (var item in items)
        {
            switch (item.Bucket)
            {
                case ParentAssignmentBucket.Completed: counts.Completed++; break;
                case ParentAssignmentBucket.Overdue: counts.Overdue++; break;
                default: counts.Pending++; break;
            }
        }

        return counts;
    }

    /// <summary>
    /// Öğretmen durumundan veli kovasına: Completed → Completed; Expired (teslim tarihi geçti ya da oturumun süresi doldu) →
    /// Overdue; başlanmış ama teslim tarihi geçmiş (InProgress) → Overdue; diğerleri (NotStarted / InProgress, süre devam
    /// ediyor) → Pending.
    /// </summary>
    internal static ParentAssignmentBucket ToBucket(string status, DateTime? endAt, DateTime now) => status switch
    {
        AssignmentStudentStatuses.Completed => ParentAssignmentBucket.Completed,
        AssignmentStudentStatuses.Expired => ParentAssignmentBucket.Overdue,
        _ when endAt.HasValue && endAt.Value < now => ParentAssignmentBucket.Overdue,
        _ => ParentAssignmentBucket.Pending
    };
}
