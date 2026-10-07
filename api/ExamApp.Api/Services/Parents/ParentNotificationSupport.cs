using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Parents;

/// <summary>Bildirim event'inde taşınan kullanıcı kimliği: Keycloak sub (SignalR hedefi) + kısa görünen ad ("Ad S.").</summary>
public readonly record struct NotificationUser(string KeycloakId, string DisplayName)
{
    public static readonly NotificationUser Unknown = new(string.Empty, string.Empty);
}

/// <summary>Öğrencinin o an Active bağlantılı velisi (bildirim alıcısı).</summary>
public sealed record ActiveParentRecipient(int ParentId, int ParentUserId, DateTime ActivatedAt);

/// <summary>
/// Issue #423 (epic #407 V5): veli bildirim event'lerini üreten exam API tarafı için ortak yardımcılar. Servisler arası veri
/// paylaşımı yasak olduğundan (veli–öğrenci bağlantıları exam DB'de) "hangi veliler" kararı burada, event yazılırken verilir;
/// BadgeService yalnız tek alıcılı event'i tüketir.
/// </summary>
public static class ParentNotificationSupport
{
    /// <summary>Ad/sub çözümü (auth-api) üst süresi. Aşılırsa boş sözlük — event yine yazılır, consumer yedeğe düşer.</summary>
    public static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Öğrencinin Active bağlantılı (silinmemiş) velileri. Pending/Revoked bağlantılar ASLA dahil değildir.
    /// </summary>
    public static async Task<List<ActiveParentRecipient>> ActiveParentsAsync(AppDbContext context, int studentId, CancellationToken ct)
    {
        var rows = await context.ParentStudentLinks.AsNoTracking()
            .Where(l => l.StudentId == studentId
                && l.Status == ParentStudentLinkStatus.Active
                && !l.Parent.IsDeleted)
            .Select(l => new { l.ParentId, ParentUserId = l.Parent.UserId, l.ActivatedAt, l.CreatedAt })
            .ToListAsync(ct);
        return rows
            .Select(r => new ActiveParentRecipient(r.ParentId, r.ParentUserId, r.ActivatedAt ?? r.CreatedAt))
            .ToList();
    }

    /// <summary>
    /// auth-api'den toplu sub + kısa ad çözümü (best-effort, <see cref="LookupTimeout"/> üst süreli). auth-api
    /// erişilemez/yavaşsa boş sözlük döner (fail-soft): event boş sub/adla yazılır, consumer BadgeService verisinden çözer.
    /// İsteğin kendisi (dış ct) iptal edildiyse iptal yayılır.
    /// </summary>
    public static async Task<IReadOnlyDictionary<int, NotificationUser>> LookupAsync(
        IAuthApiClient? authApi, IEnumerable<int> userIds, ILogger? logger, CancellationToken ct, TimeSpan? timeoutOverride = null)
    {
        var result = new Dictionary<int, NotificationUser>();
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (authApi == null || ids.Count == 0)
            return result;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutOverride ?? LookupTimeout);
        try
        {
            var users = await authApi.GetUsersByIdsAsync(ids, timeout.Token);
            foreach (var u in users)
            {
                result[u.Id] = new NotificationUser(
                    u.KeycloakId ?? string.Empty,
                    WorksheetCommentService.FormatStudentDisplayName(u.FullName) ?? string.Empty);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger?.LogWarning(ex, "[ParentNotifications] Kullanıcı adı/sub çözülemedi ({Count} kullanıcı); event boş değerlerle yazılacak.", ids.Count);
            result.Clear();
        }

        return result;
    }

    /// <summary>
    /// issue #423 (security M1): event yazan transaction'ın İÇİNDE Active veli id'lerini yeniden okur. PostgreSQL'de satırlar
    /// <c>FOR SHARE</c> ile kilitlenir (parametreli): koparma (koşullu UPDATE → satır kilidi) önce gelmişse bu okuma commit'ini
    /// bekler ve Revoked satırı görür (READ COMMITTED yeniden değerlendirme) → o veliye event yazılmaz; bu okuma önce gelmişse
    /// koparma, event'leri yazan transaction commit olana dek bekler. Postgres dışında (SQLite birim testleri) kilitsiz aynı koşul.
    /// Çağıran, transaction öncesi planladığı set ile bu setin KESİŞİMİne yazar (sonradan Active olan veli zaten plana girmemiştir).
    /// </summary>
    public static async Task<HashSet<int>> ActiveParentIdsLockedAsync(AppDbContext context, int studentId, CancellationToken ct)
    {
        if (!context.Database.IsNpgsql())
        {
            var plain = await context.ParentStudentLinks.AsNoTracking()
                .Where(l => l.StudentId == studentId && l.Status == ParentStudentLinkStatus.Active)
                .Select(l => l.ParentId)
                .ToListAsync(ct);
            return plain.ToHashSet();
        }

        var active = nameof(ParentStudentLinkStatus.Active);
        var rows = await context.Database
            .SqlQuery<int>($"SELECT l.\"ParentId\" AS \"Value\" FROM \"ParentStudentLinks\" l WHERE l.\"StudentId\" = {studentId} AND l.\"Status\" = {active} FOR SHARE OF l")
            .ToListAsync(ct);
        return rows.ToHashSet();
    }

    public static NotificationUser Of(IReadOnlyDictionary<int, NotificationUser> users, int userId)
        => users.TryGetValue(userId, out var u) ? u : NotificationUser.Unknown;
}
