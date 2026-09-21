using System.Globalization;
using System.Text.Json;
using ExamApp.Foundation.Localization;

namespace ExamApp.Api.Tests.Localization;

/// <summary>
/// Tests for resource file integrity (issue #184). Verifies that:
/// - Each domain (e.g., questions.tr.json) has a corresponding file in all supported languages
/// - Flattened key sets match exactly between languages
/// - No duplicate keys exist across domains
/// - Placeholder syntax ({n}) matches across languages
/// </summary>
public class ResourcesIntegrityTests
{
    private const string ResourcesFolder = "Resources";

    // ---- Helper to find Resources folder ----

    private static string FindResourcesFolder()
    {
        // Start from the app's base directory and search upward for api/ExamApp.Api/Resources
        var baseDir = AppContext.BaseDirectory;
        var current = new DirectoryInfo(baseDir);

        while (current != null)
        {
            var resourcePath = Path.Combine(current.FullName, "api", "ExamApp.Api", "Resources");
            if (Directory.Exists(resourcePath))
            {
                return resourcePath;
            }

            current = current.Parent;
        }

        // If not found, return a path that will fail the test
        return null!;
    }

    // ---- Domain pairing tests ----

    [Fact]
    public void Each_language_domain_has_matching_files_in_all_supported_languages()
    {
        // Arrange
        var resourcesPath = FindResourcesFolder();
        if (resourcesPath is null)
        {
            Assert.Skip("Resources folder not found in expected location");
        }

        var dir = new DirectoryInfo(resourcesPath);
        var files = dir.GetFiles("*.json", SearchOption.AllDirectories);

        var filesByDomain = files
            .Where(f => !f.Name.StartsWith("."))
            .GroupBy(f => GetDomainName(f.Name))
            .ToList();

        // Act & Assert: for each domain, ensure files exist in all supported languages
        foreach (var domain in filesByDomain)
        {
            var languages = domain.Select(f => GetLanguageFromFileName(f.Name)).Distinct().ToList();

            foreach (var lang in SupportedLocales.All)
            {
                languages.ShouldContain(lang,
                    $"Domain '{domain.Key}' missing file for language '{lang}'");
            }
        }
    }

    // ---- Flattened key matching ----

    [Fact]
    public void Flattened_key_sets_match_exactly_between_language_files()
    {
        // Arrange
        var resourcesPath = FindResourcesFolder();
        if (resourcesPath is null)
        {
            Assert.Skip("Resources folder not found in expected location");
        }

        var dir = new DirectoryInfo(resourcesPath);
        var jsonFiles = dir.GetFiles("*.json", SearchOption.AllDirectories)
            .Where(f => !f.Name.StartsWith("."))
            .ToList();

        var filesByDomain = jsonFiles
            .GroupBy(f => GetDomainName(f.Name))
            .ToList();

        // Act & Assert
        foreach (var domain in filesByDomain)
        {
            var filesByLang = domain
                .GroupBy(f => GetLanguageFromFileName(f.Name))
                .ToDictionary(g => g.Key, g => g.First());

            string? firstLanguage = null;
            IReadOnlySet<string>? firstKeySet = null;

            foreach (var (lang, file) in filesByLang)
            {
                var keys = ExtractFlattenedKeys(file.FullName);

                if (firstLanguage is null)
                {
                    firstLanguage = lang;
                    firstKeySet = keys;
                }
                else
                {
                    var missing = firstKeySet!.Except(keys).ToList();
                    var extra = keys.Except(firstKeySet!).ToList();

                    missing.ShouldBeEmpty(
                        $"Domain '{domain.Key}': language '{lang}' missing keys from '{firstLanguage}': {string.Join(", ", missing)}");
                    extra.ShouldBeEmpty(
                        $"Domain '{domain.Key}': language '{lang}' has extra keys not in '{firstLanguage}': {string.Join(", ", extra)}");
                }
            }
        }
    }

    // ---- Duplicate keys across domains ----

    [Fact]
    public void No_key_is_duplicated_across_different_domains()
    {
        // Arrange
        var resourcesPath = FindResourcesFolder();
        if (resourcesPath is null)
        {
            Assert.Skip("Resources folder not found in expected location");
        }

        var dir = new DirectoryInfo(resourcesPath);
        var jsonFiles = dir.GetFiles("*.json", SearchOption.AllDirectories)
            .Where(f => !f.Name.StartsWith("."))
            .ToList();

        var allKeys = new Dictionary<string, (string Domain, string Language, string FilePath)>(
            StringComparer.OrdinalIgnoreCase);

        // Act: collect all keys
        foreach (var file in jsonFiles)
        {
            var domain = GetDomainName(file.Name);
            var lang = GetLanguageFromFileName(file.Name);
            var keys = ExtractFlattenedKeys(file.FullName);

            foreach (var key in keys)
            {
                if (allKeys.TryGetValue(key, out var existing))
                {
                    // Assert: same key in different domain
                    existing.Domain.ShouldBe(domain,
                        $"Key '{key}' found in both domain '{existing.Domain}' ({existing.FilePath}) " +
                        $"and domain '{domain}' ({file.FullName})");
                }

                allKeys[key] = (domain, lang, file.FullName);
            }
        }
    }

    // ---- Placeholder consistency ----

    [Fact]
    public void Placeholder_syntax_matches_across_language_files()
    {
        // Arrange
        var resourcesPath = FindResourcesFolder();
        if (resourcesPath is null)
        {
            Assert.Skip("Resources folder not found in expected location");
        }

        var dir = new DirectoryInfo(resourcesPath);
        var jsonFiles = dir.GetFiles("*.json", SearchOption.AllDirectories)
            .Where(f => !f.Name.StartsWith("."))
            .ToList();

        var filesByDomain = jsonFiles
            .GroupBy(f => GetDomainName(f.Name))
            .ToList();

        // Act & Assert
        foreach (var domain in filesByDomain)
        {
            var filesByLang = domain
                .GroupBy(f => GetLanguageFromFileName(f.Name))
                .ToDictionary(g => g.Key, g => g.First());

            var keysWithPlaceholders = GetKeysWithPlaceholders(filesByLang.First().Value.FullName);

            foreach (var (lang, file) in filesByLang)
            {
                var langPlaceholders = GetKeysWithPlaceholders(file.FullName);

                foreach (var key in keysWithPlaceholders.Keys)
                {
                    if (langPlaceholders.TryGetValue(key, out var langPhs))
                    {
                        var firstPhs = keysWithPlaceholders[key];
                        langPhs.ShouldBe(firstPhs,
                            $"Domain '{domain.Key}', key '{key}': placeholders differ between languages. " +
                            $"First language has {firstPhs}, language '{lang}' has {langPhs}");
                    }
                }
            }
        }
    }

    // ---- JSON validity ----

    [Fact]
    public void All_resource_files_are_valid_json()
    {
        // Arrange
        var resourcesPath = FindResourcesFolder();
        if (resourcesPath is null)
        {
            Assert.Skip("Resources folder not found in expected location");
        }

        var dir = new DirectoryInfo(resourcesPath);
        var jsonFiles = dir.GetFiles("*.json", SearchOption.AllDirectories)
            .Where(f => !f.Name.StartsWith("."))
            .ToList();

        // Act & Assert
        foreach (var file in jsonFiles)
        {
            try
            {
                using var stream = File.OpenRead(file.FullName);
                using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                document.RootElement.ValueKind.ShouldBe(JsonValueKind.Object,
                    $"Resource file '{file.Name}' root must be a JSON object");
            }
            catch (JsonException ex)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Resource file '{file.Name}' contains invalid JSON: {ex.Message}");
            }
        }
    }

    // ---- Helper methods ----

    private static string GetDomainName(string fileName)
    {
        // "questions.tr.json" -> "questions"
        var withoutExt = fileName[..^".json".Length];
        var lastDot = withoutExt.LastIndexOf('.');
        return lastDot > 0 ? withoutExt[..lastDot] : withoutExt;
    }

    private static string GetLanguageFromFileName(string fileName)
    {
        // "questions.tr.json" -> "tr"
        var withoutExt = fileName[..^".json".Length];
        var lastDot = withoutExt.LastIndexOf('.');
        return lastDot > 0 ? withoutExt[(lastDot + 1)..] : "";
    }

    private static IReadOnlySet<string> ExtractFlattenedKeys(string filePath)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var stream = File.OpenRead(filePath);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        FlattenElement(document.RootElement, prefix: null, keys);

        return keys;
    }

    private static void FlattenElement(JsonElement element, string? prefix, HashSet<string> keys)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = prefix is null ? property.Name : $"{prefix}.{property.Name}";
                    FlattenElement(property.Value, key, keys);
                }
                break;

            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (prefix is not null)
                {
                    keys.Add(prefix);
                }
                break;
        }
    }

    private static Dictionary<string, string> GetKeysWithPlaceholders(string filePath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var stream = File.OpenRead(filePath);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });

        CollectPlaceholders(document.RootElement, prefix: null, result);

        return result;
    }

    private static void CollectPlaceholders(JsonElement element, string? prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = prefix is null ? property.Name : $"{prefix}.{property.Name}";
                    CollectPlaceholders(property.Value, key, result);
                }
                break;

            case JsonValueKind.String:
                var stringValue = element.GetString();
                if (prefix is not null && stringValue is not null)
                {
                    // Extract placeholder pattern (all {n} occurrences)
                    var placeholders = System.Text.RegularExpressions.Regex.Matches(stringValue, @"\{\d+\}")
                        .Cast<System.Text.RegularExpressions.Match>()
                        .Select(m => m.Value)
                        .OrderBy(p => p)
                        .ToList();

                    if (placeholders.Count > 0)
                    {
                        result[prefix] = string.Join(",", placeholders);
                    }
                }
                break;
        }
    }
}
