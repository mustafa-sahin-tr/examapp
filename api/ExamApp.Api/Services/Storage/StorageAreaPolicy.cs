using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Services.Storage;

/// <summary>
/// issue #365 (S2): <see cref="StorageArea"/> → bucket/prefix allowlist'i ve istemciden gelen görsel adresinin
/// normalizasyonu. İki iş için tek kaynak:
/// <list type="bullet">
/// <item><see cref="IsAllowed"/>: imzalayıcı yalnız allowlist'teki nesneleri imzalar.</item>
/// <item><see cref="TryNormalizeClientUrl"/>: istemcinin gönderdiği adres DB'ye yazılmadan önce
/// <c>/img/{bucket}/{key}</c> biçimine indirgenir (query/fragment — dolayısıyla imza — atılır) ve alanın
/// allowlist'ine uymuyorsa reddedilir. Böylece istemci, saklanan alana <c>question-transfer/...</c> gibi bir anahtar
/// yazıp sonra imzalatamaz ("imza kâhini" kapanır) ve imzalı URL asla saklanmaz.</item>
/// </list>
/// </summary>
public sealed class StorageAreaPolicy
{
    /// <summary>Soru bankası paketleri: hiçbir bucket'ta, hiçbir alan için imzalanmaz/kabul edilmez.</summary>
    public const string QuestionTransferPrefix = "question-transfer/";

    /// <summary>Saklanan adres için makul üst sınır (DB'deki gerçek değerler ~100 karakter).</summary>
    public const int MaxUrlLength = 1024;

    /// <summary>Paragraf görsellerinin varsayılan bucket'taki prefix'i (istemciden yalnız bu kabul edilir, #365 D1).</summary>
    public const string PassageImagePrefix = "passages/";

    private readonly IReadOnlyDictionary<StorageArea, IReadOnlyList<(string Bucket, string Prefix)>> _rules;

    public StorageAreaPolicy(IConfiguration configuration)
        : this(configuration.GetSection("MinioConfig")["BucketName"])
    {
    }

    /// <param name="defaultBucket"><c>MinioConfig:BucketName</c> (soru/şık/paragraf görsellerinin bucket'ı).</param>
    public StorageAreaPolicy(string? defaultBucket)
    {
        var questions = string.IsNullOrWhiteSpace(defaultBucket) ? MinioBucketPolicies.DefaultBucket : defaultBucket.Trim();
        _rules = new Dictionary<StorageArea, IReadOnlyList<(string, string)>>
        {
            [StorageArea.QuestionImage] = MinioBucketPolicies.DefaultBucketImagePrefixes.Select(p => (questions, p)).ToList(),
            // Bu iki bucket'a yalnız worksheet kapak görselleri yazılıyor ({worksheetId}-background.ext, {guid}.jpg).
            [StorageArea.WorksheetCover] = [(MinioBucketPolicies.WorksheetsBucket, ""), (MinioBucketPolicies.ExamsBucket, "")],
            [StorageArea.StudyPage] = [(MinioBucketPolicies.StudyPagesBucket, "books/"), (MinioBucketPolicies.StudyPagesBucket, "pages/")],
        };
    }

    /// <summary>Nesne verilen alanlardan en az birinin allowlist'inde mi? <c>question-transfer/</c> her zaman hayır.</summary>
    public bool IsAllowed(string bucket, string key, IReadOnlyList<StorageArea> areas)
    {
        if (string.IsNullOrEmpty(bucket) || string.IsNullOrEmpty(key) ||
            key.StartsWith(QuestionTransferPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var area in areas)
        {
            if (!_rules.TryGetValue(area, out var rules))
                continue;
            foreach (var (b, prefix) in rules)
            {
                if (string.Equals(b, bucket, StringComparison.Ordinal) &&
                    key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// İstemciden gelen görsel adresini saklanacak biçime indirger. Boş/null girdi geçerlidir (<paramref name="normalized"/>
    /// null). Kabul edilen tek biçim göreli <c>/img/{bucket}/{key}</c>; query ve fragment atılır, yol bir kez
    /// percent-decode edilir (imzalı URL'den geri gelen <c>%20</c>/<c>%C4%B1</c> ham anahtara döner). Tam URL,
    /// <c>javascript:</c>, <c>//host</c>, <c>..</c>, ters bölü, kontrol karakteri, yabancı bucket/prefix ve
    /// <c>question-transfer/</c> reddedilir.
    /// </summary>
    public bool TryNormalizeClientUrl(string? input, StorageArea area, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(input))
            return true;

        var value = input.Trim();
        if (value.Length > MaxUrlLength)
            return false;

        var cut = value.IndexOfAny(['?', '#']);
        if (cut >= 0)
            value = value[..cut];

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (decoded.Any(char.IsControl) || decoded.Contains('?') || decoded.Contains('#'))
            return false;

        // Saklanan her değer imzalanabilir olmalı: gateway'den imzası bozulmadan geçemeyecek anahtar (&, %) da reddedilir.
        if (!MinioObjectUrl.TryParse(decoded, out var bucket, out var key) || !IsAllowed(bucket, key, [area]) ||
            !MinioObjectUrl.IsGatewaySafeKey(key))
            return false;

        normalized = MinioObjectUrl.Build(bucket, key);
        return true;
    }

    /// <summary><see cref="TryNormalizeClientUrl"/> + boş girdiyi reddeden kısayol.</summary>
    public bool TryNormalizeRequiredClientUrl(string? input, StorageArea area, [NotNullWhen(true)] out string? normalized)
        => TryNormalizeClientUrl(input, area, out normalized) && normalized is not null;

    /// <summary>
    /// <see cref="TryNormalizeClientUrl(string?, StorageArea, out string?)"/> + anahtarın <paramref name="requiredKeyPrefix"/>
    /// ile başlaması şartı (alan allowlist'inin daraltılmış hâli). Örn. paragraf görseli yalnız <c>passages/</c> altından
    /// kabul edilir; aynı alandaki <c>questions/</c> ya da <c>answers/</c> nesnesine işaret eden adres reddedilir (#365 D1).
    /// Boş girdi geçerlidir (<paramref name="normalized"/> null).
    /// </summary>
    public bool TryNormalizeClientUrl(string? input, StorageArea area, string requiredKeyPrefix, out string? normalized)
    {
        ArgumentException.ThrowIfNullOrEmpty(requiredKeyPrefix);
        if (!TryNormalizeClientUrl(input, area, out normalized))
            return false;
        if (normalized is null)
            return true;

        if (MinioObjectUrl.TryParse(normalized, out _, out var key) &&
            key.StartsWith(requiredKeyPrefix, StringComparison.Ordinal) && key.Length > requiredKeyPrefix.Length)
            return true;

        normalized = null;
        return false;
    }
}
