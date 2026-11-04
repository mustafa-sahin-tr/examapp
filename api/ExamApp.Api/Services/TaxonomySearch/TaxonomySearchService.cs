using System.Globalization;
using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.TaxonomySearch;

public interface ITaxonomySearchService
{
    Task<List<TaxonomySearchResultDto>> SearchAsync(string query, int? gradeId, int take, CancellationToken ct = default);

    /// <summary>Öğrencinin sınıfı: aramanın varsayılanı. Tam profil gerekmez, her aramada okunur.</summary>
    Task<int?> GetStudentGradeIdAsync(int userId, CancellationToken ct = default);
}

/// <summary>
/// Issue #499: "matematik açılar" gibi serbest metni ders, konu ya da alt konuya çevirir. Eşleşme bellekte yapılır: Türkçe
/// harfler DB harmanlamasına bırakılmaz, `unaccent` eklentisi ve türetilmiş sütun gerekmez. Bedeli her aramada adların
/// okunması; bu yüzden oran sınırı var ve okunan sütunlar yalnız ad ve kimliktir.
/// <para>Her sorgu sözcüğü yolda (ders + konu + alt konu) geçmeli, yolun son parçasında da en az biri. Böylece "matematik
/// açılar" Açılar konusunu bulur, altındaki her alt konuyu dökmez.</para>
/// </summary>
public sealed class TaxonomySearchService(AppDbContext db) : ITaxonomySearchService
{
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 100;
    public const int MaxTake = 20;

    private const string PathSeparator = " › ";
    public const int MaxQueryTokens = 8;

    /// <summary>"açıları" sorgusu "Açılar" adını bulsun diye; kısa adlarda ek sayılmaz ("su" sorgusu "sulama"yı değil "su"yu arar).</summary>
    private const int MinStemLength = 4;

    private const int LeafMatch = 3;
    private const int ParentMatch = 2;
    private const int ExactBonus = 1;
    private const int SubjectAndLeafBonus = 2;

    /// <summary>Küçük harf, Türkçe ve diğer aksanlar sade (İ, I, ı → i; ş → s; ğ → g; ç → c; ö → o; ü → u).</summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(c == 'ı' ? 'i' : char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    public static string[] Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var c in Normalize(text))
        {
            // Kesme işareti sözcüğü bölmez: "Kur'an" "kuran", "Türkiye'nin" "turkiyenin" olur.
            if (c is '\'' or '’' or 'ʼ')
                continue;

            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
                continue;
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());
            current.Clear();
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens.Distinct().ToArray();
    }

    public static bool IsValidQuery(string? query)
    {
        var trimmed = query?.Trim();
        if (trimmed is not { Length: >= MinQueryLength and <= MaxQueryLength })
            return false;

        // Uzunluk harf ve rakamla ölçülür ("a." tek karakterdir); fazla sözcük sessizce atılsa eşleşme gevşerdi.
        var tokens = Tokenize(trimmed);
        return tokens.Length <= MaxQueryTokens && tokens.Sum(t => t.Length) >= MinQueryLength;
    }

    private sealed record Part(string Name, string[] Tokens);

    private sealed record Candidate(int SubjectId, int? TopicId, int? SubTopicId, int GradeId, string GradeName, Part[] Parts);

    public Task<int?> GetStudentGradeIdAsync(int userId, CancellationToken ct = default) =>
        db.Students.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.GradeId).FirstOrDefaultAsync(ct);

    public async Task<List<TaxonomySearchResultDto>> SearchAsync(string query, int? gradeId, int take, CancellationToken ct = default)
    {
        var tokens = Tokenize(query);
        if (tokens.Length == 0)
            return [];

        var topics = await db.Topics.AsNoTracking()
            .Where(t => gradeId == null || t.GradeId == gradeId)
            .Select(t => new { t.Id, t.Name, t.SubjectId, SubjectName = t.Subject.Name, t.GradeId, GradeName = t.Grade.Name })
            .ToListAsync(ct);
        var topicIds = topics.Select(t => t.Id).ToList();
        var subTopics = await db.SubTopics.AsNoTracking()
            .Where(s => topicIds.Contains(s.TopicId))
            .Select(s => new { s.Id, s.Name, s.TopicId })
            .ToListAsync(ct);

        var subjectParts = topics.Select(t => t.SubjectName).Distinct().ToDictionary(n => n, n => new Part(n, Tokenize(n)));
        var topicParts = topics.ToDictionary(t => t.Id, t => new Part(t.Name, Tokenize(t.Name)));

        var candidates = new List<Candidate>();
        // Ders, sınıf başına bir sonuçtur: konusu olmayan ders o sınıfta aranacak bir şey sunmaz.
        candidates.AddRange(topics
            .DistinctBy(t => (t.SubjectId, t.GradeId))
            .Select(t => new Candidate(t.SubjectId, null, null, t.GradeId, t.GradeName, [subjectParts[t.SubjectName]])));
        candidates.AddRange(topics
            .Select(t => new Candidate(t.SubjectId, t.Id, null, t.GradeId, t.GradeName, [subjectParts[t.SubjectName], topicParts[t.Id]])));
        var topicsById = topics.ToDictionary(t => t.Id);
        candidates.AddRange(subTopics.Select(s =>
        {
            var topic = topicsById[s.TopicId];
            return new Candidate(topic.SubjectId, topic.Id, s.Id, topic.GradeId, topic.GradeName,
                [subjectParts[topic.SubjectName], topicParts[topic.Id], new Part(s.Name, Tokenize(s.Name))]);
        }));

        return candidates
            .Select(c => (Candidate: c, Score: ScoreOf(c.Parts, tokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            // Eşitlikte üst düzey önce: ders, konu, alt konu; sonra alt sınıf ve ad: sonuç sırası kararlı.
            .ThenBy(x => x.Candidate.Parts.Length)
            .ThenBy(x => x.Candidate.GradeId)
            .ThenBy(x => x.Candidate.Parts[^1].Name, StringComparer.Ordinal)
            .Take(take)
            .Select(x => new TaxonomySearchResultDto
            {
                SubjectId = x.Candidate.SubjectId,
                TopicId = x.Candidate.TopicId,
                SubTopicId = x.Candidate.SubTopicId,
                GradeId = x.Candidate.GradeId,
                GradeName = x.Candidate.GradeName,
                Path = string.Join(PathSeparator, x.Candidate.Parts.Select(p => p.Name)),
                Score = x.Score,
            })
            .ToList();
    }

    /// <summary>Eşleşmeyen sözcük varsa ya da son parça hiç eşleşmediyse 0.</summary>
    private static int ScoreOf(Part[] parts, string[] queryTokens)
    {
        var score = 0;
        var leafTokens = new HashSet<string>();
        var subjectTokens = new HashSet<string>();
        foreach (var query in queryTokens)
        {
            var best = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                var isLeaf = i == parts.Length - 1;
                var match = MatchOf(parts[i].Tokens, query);
                if (match == 0)
                    continue;
                if (isLeaf)
                    leafTokens.Add(query);
                else if (i == 0)
                    subjectTokens.Add(query);
                best = Math.Max(best, (isLeaf ? LeafMatch : ParentMatch) + (match == 2 ? ExactBonus : 0));
            }

            if (best == 0)
                return 0;
            score += best;
        }

        if (leafTokens.Count == 0)
            return 0;

        // Ödül farklı sözcüklere: "matematik" tek başına "Matematik Problemleri"ni dersin önüne geçirmez.
        var subjectAndLeaf = subjectTokens.Any(subject => leafTokens.Any(leaf => leaf != subject));
        return subjectAndLeaf ? score + SubjectAndLeafBonus : score;
    }

    /// <returns>2 tam sözcük, 1 önek ya da ek almış hali, 0 eşleşme yok.</returns>
    private static int MatchOf(string[] nameTokens, string query)
    {
        var result = 0;
        foreach (var name in nameTokens)
        {
            if (name == query)
                return 2;
            if (name.StartsWith(query, StringComparison.Ordinal)
                || (name.Length >= MinStemLength && query.StartsWith(name, StringComparison.Ordinal)))
                result = 1;
        }

        return result;
    }
}
