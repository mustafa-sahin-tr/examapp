using System.Text.Json;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.QuestionTransfer;

/// <summary>
/// Paketteki ders, konu ve alt konuyu hedefte bulur: önce kataloğun kalıcı koduyla, kod yoksa (eski paket) adla.
/// Bulunamayan sınıflandırma <see cref="Missing"/> olarak işaretlenir, sessizce boş kalmaz.
/// </summary>
public sealed class QuestionImportTaxonomy(AppDbContext db)
{
    private readonly Dictionary<string, int?> _subjects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _topics = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _subTopics = new(StringComparer.OrdinalIgnoreCase);

    public sealed record Resolved(int? SubjectId, int? TopicId, List<int> SubTopicIds, bool Missing);

    public async Task<Resolved> ResolveAsync(JsonElement question, CancellationToken ct = default)
    {
        var missing = false;

        int? subjectId = null;
        if (question.TryGetProperty("subject", out var subjectEl) && subjectEl.ValueKind == JsonValueKind.Object)
        {
            subjectId = await ResolveSubjectAsync(subjectEl, ct);
            missing |= subjectId is null;
        }

        int? topicId = null;
        if (question.TryGetProperty("topic", out var topicEl) && topicEl.ValueKind == JsonValueKind.Object)
        {
            topicId = await ResolveTopicAsync(topicEl, subjectId, ct);
            missing |= topicId is null;
        }

        var subTopicIds = new List<int>();
        if (question.TryGetProperty("subTopics", out var subTopicsEl) && subTopicsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var subTopicEl in subTopicsEl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                var id = await ResolveSubTopicAsync(subTopicEl, ct);
                if (id is null)
                    missing = true;
                else if (!subTopicIds.Contains(id.Value))
                    subTopicIds.Add(id.Value);
            }
        }

        return new Resolved(subjectId, topicId, subTopicIds, missing);
    }

    private async Task<int?> ResolveSubjectAsync(JsonElement el, CancellationToken ct)
    {
        var code = Read(el, "code");
        var name = Read(el, "name");
        var key = $"{code}|{name}";
        if (_subjects.TryGetValue(key, out var cached))
            return cached;

        int? id = null;
        if (code is not null)
            id = await db.Subjects.AsNoTracking().Where(s => s.Code == code).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        if (id is null && name is not null)
        {
            var lowered = name.ToLower();
            id = await db.Subjects.AsNoTracking().Where(s => s.Name.ToLower() == lowered).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        }

        return _subjects[key] = id;
    }

    private async Task<int?> ResolveTopicAsync(JsonElement el, int? subjectId, CancellationToken ct)
    {
        var code = Read(el, "code");
        var name = Read(el, "name");
        var gradeName = Read(el, "gradeName");
        var key = $"{code}|{subjectId}|{gradeName}|{name}";
        if (_topics.TryGetValue(key, out var cached))
            return cached;

        int? id = null;
        if (code is not null)
            id = await db.Topics.AsNoTracking().Where(t => t.Code == code).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        if (id is null && name is not null)
        {
            var lowered = name.ToLower();
            var query = db.Topics.AsNoTracking().Where(t => t.Name.ToLower() == lowered);
            if (subjectId is not null)
                query = query.Where(t => t.SubjectId == subjectId);
            if (gradeName is not null)
            {
                var loweredGrade = gradeName.ToLower();
                query = query.Where(t => t.Grade.Name.ToLower() == loweredGrade);
            }
            id = await query.OrderBy(t => t.Id).Select(t => (int?)t.Id).FirstOrDefaultAsync(ct);
        }

        return _topics[key] = id;
    }

    private async Task<int?> ResolveSubTopicAsync(JsonElement el, CancellationToken ct)
    {
        var code = Read(el, "code");
        var name = Read(el, "name");
        var topicName = Read(el, "topicName");
        var gradeName = Read(el, "gradeName");
        var key = $"{code}|{gradeName}|{topicName}|{name}";
        if (_subTopics.TryGetValue(key, out var cached))
            return cached;

        int? id = null;
        if (code is not null)
            id = await db.SubTopics.AsNoTracking().Where(s => s.Code == code).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        if (id is null && name is not null)
        {
            var lowered = name.ToLower();
            var query = db.SubTopics.AsNoTracking().Where(s => s.Name.ToLower() == lowered);
            if (topicName is not null)
            {
                var loweredTopic = topicName.ToLower();
                query = query.Where(s => s.Topic.Name.ToLower() == loweredTopic);
            }
            if (gradeName is not null)
            {
                var loweredGrade = gradeName.ToLower();
                query = query.Where(s => s.Topic.Grade.Name.ToLower() == loweredGrade);
            }
            id = await query.OrderBy(s => s.Id).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        }

        return _subTopics[key] = id;
    }

    private static string? Read(JsonElement el, string name)
    {
        var value = el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
