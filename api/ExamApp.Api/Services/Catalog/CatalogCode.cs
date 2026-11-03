using System.Text;
using ExamApp.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Catalog;

/// <summary>
/// Sınıf, ders, konu ve alt konunun ortamlar arasında değişmeyen kodu. Kayıt oluşurken addan üretilir ve ad değişse de korunur.
/// <see cref="Slug"/> ile <c>AddCatalogCodes</c> migration'ındaki SQL aynı sonucu vermelidir: aksi halde aynı veriye sahip iki ortam farklı kod alır.
/// </summary>
public static class CatalogCode
{
    public const int MaxLength = 400;
    private const int SlugMaxLength = 120;
    private const string TurkishFrom = "ÇçĞğİıÖöŞşÜüÂâÎîÛû";
    private const string TurkishTo = "ccggiioossuuaaiiuu";

    public static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        var pendingDash = false;
        foreach (var raw in name)
        {
            var index = TurkishFrom.IndexOf(raw);
            var c = char.ToLowerInvariant(index >= 0 ? TurkishTo[index] : raw);
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                pendingDash = false;
                builder.Append(c);
            }
            else
            {
                pendingDash = true;
            }
        }

        var slug = builder.Length > SlugMaxLength ? builder.ToString(0, SlugMaxLength) : builder.ToString();
        return slug.Trim('-');
    }

    public static string ForTopic(string gradeCode, string subjectCode, string name) => $"{gradeCode}.{subjectCode}.{Slug(name)}";

    public static string ForSubTopic(string topicCode, string name) => $"{topicCode}.{Slug(name)}";

    public static async Task<string> ForNewSubjectAsync(AppDbContext context, string name, CancellationToken ct)
    {
        var code = Slug(name);
        var taken = await context.Subjects.AsNoTracking().Where(s => s.Code != null && s.Code.StartsWith(code)).Select(s => s.Code!).ToListAsync(ct);
        return FirstFree(code, taken);
    }

    public static async Task<string> ForNewTopicAsync(AppDbContext context, int gradeId, int subjectId, string name, CancellationToken ct)
    {
        var grade = await context.Grades.AsNoTracking().Where(g => g.Id == gradeId).Select(g => new { g.Code, g.Name }).SingleAsync(ct);
        var subject = await context.Subjects.AsNoTracking().Where(s => s.Id == subjectId).Select(s => new { s.Code, s.Name }).SingleAsync(ct);
        var code = ForTopic(grade.Code ?? Slug(grade.Name), subject.Code ?? Slug(subject.Name), name);
        var taken = await context.Topics.AsNoTracking().Where(t => t.Code != null && t.Code.StartsWith(code)).Select(t => t.Code!).ToListAsync(ct);
        return FirstFree(code, taken);
    }

    public static async Task<string> ForNewSubTopicAsync(AppDbContext context, int topicId, string name, CancellationToken ct)
    {
        var topic = await context.Topics.AsNoTracking().Where(t => t.Id == topicId)
            .Select(t => new { t.Code, t.Name, t.GradeId, t.SubjectId }).SingleAsync(ct);
        var topicCode = topic.Code ?? await ForNewTopicAsync(context, topic.GradeId, topic.SubjectId, topic.Name, ct);
        var code = ForSubTopic(topicCode, name);
        var taken = await context.SubTopics.AsNoTracking().Where(s => s.Code != null && s.Code.StartsWith(code)).Select(s => s.Code!).ToListAsync(ct);
        return FirstFree(code, taken);
    }

    private static string FirstFree(string code, IReadOnlyCollection<string> taken)
    {
        if (!taken.Contains(code))
            return code;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{code}-{suffix}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }
}
