using System.Reflection;
using System.Text.RegularExpressions;
using ExamApp.Foundation.Text;

namespace AssistantService.Help;

/// <summary>One article of the in-app user guide (<c>Help/&lt;role&gt;/*.md</c>, #506).</summary>
public sealed record HelpArticle(string Slug, string Title, IReadOnlyCollection<string> Roles, IReadOnlyList<string> Keywords, string Body);

public sealed record HelpHit(HelpArticle Article, int Score);

/// <summary>
/// Issue #506: the search index behind <c>search_help</c>. The articles are embedded resources (the csproj globs
/// <c>Help/**/*.md</c>), so editing or adding one rebuilds the index with the next build, no manual step. Built once at
/// startup; a malformed article fails startup instead of silently dropping out of the search.
/// <para>Matching uses the taxonomy search's Turkish normalisation (<see cref="SearchText"/>): a query word counts in the
/// title and keywords with weight 3 and in the body with weight 1, best field only; a suffixed form ("velimi",
/// "eklerim") matches its stem ("veli", "ekle"). An article needs a title or keyword hit to be returned.</para>
/// </summary>
public sealed partial class HelpIndex
{
    public const string ResourcePrefix = "help/";
    public const int MaxResults = 3;

    private const int HeadWeight = 3;
    private const int BodyWeight = 1;

    /// <summary>Question words and fillers that would match every article.</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "nasil", "ne", "neden", "nerede", "nereden", "nereye", "hangi", "kim", "nedir", "niye", "mi", "mu", "misin", "miyim",
        "bir", "ve", "veya", "ile", "ben", "beni", "benim", "bana", "sen", "sana", "bu", "su", "o", "icin", "de", "da", "ki",
        "var", "yok", "gibi", "olur", "olarak", "yapabilirim", "yaparim", "yapilir", "istiyorum", "lazim", "gerek", "acaba",
    };

    private static readonly HashSet<string> KnownRoles = new(StringComparer.Ordinal) { "Student", "Teacher", "Parent", "Admin" };

    private sealed record Entry(HelpArticle Article, string[] HeadTokens, string[] BodyTokens);

    private readonly List<Entry> _entries;

    public HelpIndex(IEnumerable<HelpArticle> articles)
    {
        _entries = [];
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in articles)
        {
            if (!slugs.Add(a.Slug))
                throw new InvalidOperationException($"Help article slug '{a.Slug}' is used twice.");
            var head = SearchText.Tokenize(a.Title + " " + string.Join(' ', a.Keywords));
            _entries.Add(new Entry(a, head, SearchText.Tokenize(a.Body)));
        }
    }

    public int Count => _entries.Count;

    public IEnumerable<HelpArticle> Articles => _entries.Select(e => e.Article);

    public static HelpIndex LoadEmbedded(Assembly? assembly = null)
    {
        assembly ??= typeof(HelpIndex).Assembly;
        var articles = new List<HelpArticle>();
        foreach (var name in assembly.GetManifestResourceNames()
                     .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            articles.Add(Parse(name, reader.ReadToEnd()));
        }

        if (articles.Count == 0)
            throw new InvalidOperationException("No help articles are embedded (Help/**/*.md).");
        return new HelpIndex(articles);
    }

    /// <summary>Front matter (<c>slug</c>, <c>title</c>, <c>roles</c>, optional <c>keywords</c>, comma separated) then Markdown.</summary>
    public static HelpArticle Parse(string source, string text)
    {
        text = text.Replace("\r\n", "\n");
        if (!text.StartsWith("---\n", StringComparison.Ordinal))
            throw Invalid(source, "must start with a '---' front matter block");
        var end = text.IndexOf("\n---\n", 3, StringComparison.Ordinal);
        if (end < 0)
            throw Invalid(source, "front matter is not closed with '---'");

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text[4..end].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
                throw Invalid(source, $"bad front matter line '{line}'");
            fields[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        var slug = Required(fields, "slug", source);
        if (!SlugPattern().IsMatch(slug))
            throw Invalid(source, $"slug '{slug}' must be lower-case kebab-case");
        var roles = List(Required(fields, "roles", source));
        if (roles.Count == 0)
            throw Invalid(source, "roles is empty");
        if (roles.FirstOrDefault(r => !KnownRoles.Contains(r)) is { } unknown)
            throw Invalid(source, $"role '{unknown}' is not a realm role");
        var body = text[(end + 5)..].Trim();
        if (body.Length == 0)
            throw Invalid(source, "body is empty");
        return new HelpArticle(slug, Required(fields, "title", source), roles,
            fields.TryGetValue("keywords", out var k) ? List(k) : [], body);
    }

    public IReadOnlyList<HelpHit> Search(string query, IReadOnlyCollection<string> roles, int take = MaxResults)
    {
        var tokens = SearchText.Tokenize(query).Where(t => t.Length >= 2 && !StopWords.Contains(t)).ToArray();
        if (tokens.Length == 0)
            return [];

        var hits = new List<HelpHit>();
        foreach (var e in _entries.Where(e => e.Article.Roles.Any(roles.Contains)))
        {
            var score = 0;
            var headHit = false;
            foreach (var token in tokens)
            {
                var head = SearchText.MatchOf(e.HeadTokens, token);
                if (head > 0)
                {
                    headHit = true;
                    score += HeadWeight * head;
                    continue;
                }

                score += BodyWeight * SearchText.MatchOf(e.BodyTokens, token);
            }

            if (headHit)
                hits.Add(new HelpHit(e.Article, score));
        }

        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.Article.Slug, StringComparer.Ordinal)
            .Take(Math.Clamp(take, 1, MaxResults)).ToList();
    }

    private static string Required(Dictionary<string, string> fields, string key, string source) =>
        fields.TryGetValue(key, out var v) && v.Length > 0 ? v : throw Invalid(source, $"'{key}' is required");

    private static List<string> List(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static InvalidOperationException Invalid(string source, string reason) =>
        new($"Help article '{source}' {reason}.");

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
