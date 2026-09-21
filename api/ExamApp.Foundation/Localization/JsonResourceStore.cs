using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Foundation.Localization;

/// <summary>
/// JSON çeviri dosyalarından üretilen, salt-okunur ve thread-safe mesaj sözlüğü (issue #184).
///
/// <para>Yükleme: <c>{ResourcesPath}/**/&lt;alan&gt;.&lt;dil&gt;.json</c> dosyalarının TÜMÜ okunur ve
/// <b>dil başına tek sözlükte</b> birleştirilir. Böylece her alan (controller/domain) kendi
/// dosyasına sahip olur — paralel çalışan geliştiriciler aynı dosyada çakışmaz — ama arama
/// tarafı tek sözlük olduğu için hızlı ve basittir.</para>
///
/// <para>İç içe JSON objeleri noktalı anahtara düzleştirilir:
/// <c>{ "questions": { "notFound": "..." } }</c> → <c>questions.notFound</c>.</para>
///
/// <para>Aynı anahtar iki farklı dosyada tanımlıysa <see cref="Load"/> hata fırlatır (istenirse
/// kapatılabilir); sessiz ezme, hangi dosyanın kazandığı belli olmadığı için yasak.</para>
/// </summary>
public sealed class JsonResourceStore
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _byCulture;

    private JsonResourceStore(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> byCulture)
    {
        _byCulture = byCulture;
    }

    /// <summary>Hiç çeviri içermeyen boş sözlük (kaynak klasörü yoksa kullanılır).</summary>
    public static JsonResourceStore Empty { get; } =
        new(new Dictionary<string, IReadOnlyDictionary<string, string>>());

    /// <summary>Yüklenen dil kodları (<c>tr</c>, <c>en</c>).</summary>
    public IReadOnlyCollection<string> Cultures => (IReadOnlyCollection<string>)_byCulture.Keys;

    /// <summary>
    /// <paramref name="resourcesPath"/> altındaki tüm JSON çeviri dosyalarını okur.
    /// Uygulama açılışında bir kez çağrılır (dosya değişikliğinde otomatik yeniden yükleme yoktur).
    /// </summary>
    public static JsonResourceStore Load(
        IFileProvider fileProvider,
        string resourcesPath,
        bool throwOnDuplicateKeys = true,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(fileProvider);
        logger ??= NullLogger.Instance;

        var accumulator = new Dictionary<string, Dictionary<string, (string Value, string SourceFile)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (path, file) in EnumerateJsonFiles(fileProvider, resourcesPath ?? string.Empty))
        {
            var culture = ParseCultureFromFileName(file.Name);
            if (culture is null)
            {
                logger.LogWarning(
                    "Çeviri dosyası atlandı: {File}. Beklenen ad deseni <alan>.<dil>.json ve dil {Supported} içinden biri olmalı.",
                    path, string.Join("/", SupportedLocales.All));
                continue;
            }

            if (!accumulator.TryGetValue(culture, out var bucket))
            {
                bucket = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
                accumulator[culture] = bucket;
            }

            using var stream = file.CreateReadStream();
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    $"Çeviri dosyası '{path}' bir JSON objesi olmalı (kök eleman {document.RootElement.ValueKind}).");
            }

            foreach (var (key, value) in Flatten(document.RootElement, prefix: null, path))
            {
                if (bucket.TryGetValue(key, out var existing))
                {
                    var message =
                        $"Çeviri anahtarı birden fazla dosyada tanımlı: '{key}' ({culture}) — " +
                        $"'{existing.SourceFile}' ve '{path}'. Anahtarı yalnızca bir dosyada tut.";
                    if (throwOnDuplicateKeys)
                    {
                        throw new InvalidOperationException(message);
                    }

                    logger.LogWarning("{Message}", message);
                }

                bucket[key] = (value, path);
            }

            logger.LogInformation("Çeviri dosyası yüklendi: {File} ({Culture})", path, culture);
        }

        var byCulture = accumulator.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string>)pair.Value.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Value,
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        return new JsonResourceStore(byCulture);
    }

    /// <summary>
    /// Anahtarı verilen kültür için çözer. Sıra: tam kültür adı (<c>en-US</c>) → dil kodu
    /// (<c>en</c>) → varsayılan dil (<see cref="SupportedLocales.Default"/>, <c>tr</c>).
    /// </summary>
    public bool TryGet(CultureInfo? culture, string key, out string value)
    {
        foreach (var candidate in CultureChain(culture))
        {
            if (_byCulture.TryGetValue(candidate, out var bucket) && bucket.TryGetValue(key, out var found))
            {
                value = found;
                return true;
            }
        }

        value = key;
        return false;
    }

    /// <summary>
    /// Verilen kültürde görünen tüm anahtarlar (fallback zinciriyle birleştirilmiş hâlde).
    /// <c>IStringLocalizer.GetAllStrings</c> için.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAll(CultureInfo? culture, bool includeParentCultures)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var chain = includeParentCultures
            ? CultureChain(culture)
            : CultureChain(culture).Take(1);

        foreach (var candidate in chain)
        {
            if (!_byCulture.TryGetValue(candidate, out var bucket))
            {
                continue;
            }

            foreach (var pair in bucket)
            {
                // Zincirde önce gelen kazanır.
                result.TryAdd(pair.Key, pair.Value);
            }
        }

        return result;
    }

    private static IEnumerable<string> CultureChain(CultureInfo? culture)
    {
        culture ??= CultureInfo.CurrentUICulture;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var current = culture; current is not null && !string.IsNullOrEmpty(current.Name); current = current.Parent)
        {
            if (seen.Add(current.Name))
            {
                yield return current.Name;
            }

            if (current.Parent == current)
            {
                break;
            }
        }

        // "en_US"/"en-Latn-US" gibi durumlarda da dil koduna in.
        var normalized = SupportedLocales.Normalize(culture.Name);
        if (seen.Add(normalized))
        {
            yield return normalized;
        }

        if (seen.Add(SupportedLocales.Default))
        {
            yield return SupportedLocales.Default;
        }
    }

    private static IEnumerable<(string Path, IFileInfo File)> EnumerateJsonFiles(IFileProvider fileProvider, string path)
    {
        var contents = fileProvider.GetDirectoryContents(path);
        if (!contents.Exists)
        {
            yield break;
        }

        foreach (var entry in contents)
        {
            var childPath = string.IsNullOrEmpty(path) ? entry.Name : $"{path}/{entry.Name}";

            if (entry.IsDirectory)
            {
                foreach (var nested in EnumerateJsonFiles(fileProvider, childPath))
                {
                    yield return nested;
                }
            }
            else if (entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                yield return (childPath, entry);
            }
        }
    }

    /// <summary><c>questions.tr.json</c> → <c>tr</c>; desteklenmeyen/eşleşmeyen adlarda <c>null</c>.</summary>
    private static string? ParseCultureFromFileName(string fileName)
    {
        var withoutExtension = fileName[..^".json".Length];
        var separator = withoutExtension.LastIndexOf('.');
        if (separator <= 0)
        {
            return null;
        }

        var cultureSegment = withoutExtension[(separator + 1)..];
        return SupportedLocales.TryNormalize(cultureSegment, out var normalized) ? normalized : null;
    }

    private static IEnumerable<(string Key, string Value)> Flatten(JsonElement element, string? prefix, string sourceFile)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = prefix is null ? property.Name : $"{prefix}.{property.Name}";
                    foreach (var nested in Flatten(property.Value, key, sourceFile))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.String:
                yield return (prefix!, element.GetString()!);
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                yield return (prefix!, element.ToString());
                break;

            default:
                throw new InvalidOperationException(
                    $"Çeviri dosyası '{sourceFile}' içindeki '{prefix}' anahtarı desteklenmeyen bir değer tipi içeriyor " +
                    $"({element.ValueKind}). Yalnızca iç içe obje ve metin değerler kullanılabilir.");
        }
    }
}
