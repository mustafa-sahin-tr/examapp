using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// issue #542 (#194 K5): admin okul değişikliğinin bildirim event'leri. Çağıranın transaction'ında, çağıranın SaveChanges'iyle
/// yazılır (okul yazımıyla atomik). Yalnız okul gerçekten değişince çağrılır (aynı okul / yalnız talep temizliği → event yok).
/// </summary>
internal static class SchoolChangeNotifications
{
    /// <summary>Okul adları (event metni için). Silinmiş / bulunmayan okul boş ad olarak gider; consumer yedek metne düşer.</summary>
    public static async Task<(string From, string To)> SchoolNamesAsync(AppDbContext context, int? fromSchoolId, int? toSchoolId, CancellationToken ct)
    {
        var ids = new[] { fromSchoolId, toSchoolId }.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var names = ids.Count == 0
            ? new Dictionary<int, string>()
            : await context.Schools.IgnoreQueryFilters().AsNoTracking()
                .Where(s => ids.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name ?? string.Empty, ct);
        string Name(int? id) => id is int v && names.TryGetValue(v, out var n) ? n : string.Empty;
        return (Name(fromSchoolId), Name(toSchoolId));
    }

    public static void Add(AppDbContext context, SchoolChangedEvent @event)
        => context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<SchoolChangedEvent>(),
            Content = JsonSerializer.Serialize(@event)
        });
}
