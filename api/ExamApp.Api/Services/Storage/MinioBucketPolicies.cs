using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ExamApp.Api.Services.Storage;

/// <summary>
/// issue #365: bilinen bucket ve anonim okunabilir prefix'leri.
/// <para>
/// <see cref="AnonymousReadPrefixes"/>: null → bucket tamamen özel (politika yok); boş string içeren liste → bucket'ın
/// tamamı; aksi halde yalnız verilen prefix'ler (<c>questions/</c> gibi, sonu <c>/</c>). S4'ten beri
/// <see cref="MinioBucketPolicies.KnownBuckets"/> yalnız null üretir; liste biçimi yalnız politika JSON üreticisi için durur.
/// </para>
/// </summary>
public sealed record MinioBucketSpec(string Name, IReadOnlyList<string>? AnonymousReadPrefixes);

/// <summary>
/// issue #365: bilinen bucket'lar ve anonim okuma tanımı. S1'de bucket geneli <c>s3:GetObject *</c> prefix bazlı geçici
/// politikaya daraltılmıştı; S4 itibarıyla HİÇBİR bucket anonim okunmaz (<see cref="MinioBucketSpec.AnonymousReadPrefixes"/>
/// hepsinde null → <see cref="MinioBucketBootstrapper"/> politikayı kaldırır). Tarayıcı görselleri yalnız kısa ömürlü
/// imzalı URL ile (<see cref="MinioStorageUrlSigner"/>), sunucu tarafı okumalar root SDK istemcisiyle yapılır.
/// </summary>
public static class MinioBucketPolicies
{
    public const string DefaultBucket = "exam-questions";
    public const string WorksheetsBucket = "worksheets";
    public const string ExamsBucket = "exams";
    public const string StudyPagesBucket = "study-pages";
    public const string StudentAvatarsBucket = "student-avatars";

    /// <summary>
    /// Varsayılan bucket'taki görsel prefix'leri (QuestionService / QuestionTransferJobRunner). S4'ten beri anonim okunmaz;
    /// yalnız imza allowlist'i (<see cref="StorageAreaPolicy"/>) ve presign hesabının politikası için kullanılır.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultBucketImagePrefixes = ["questions/", "answers/", "passages/"];

    /// <summary>
    /// Uygulamanın yazdığı tüm bucket'lar. <paramref name="defaultBucket"/> = <c>MinioConfig:BucketName</c>
    /// (soru/şık/paragraf görselleri + question-transfer paketleri aynı bucket'ta).
    /// </summary>
    public static IReadOnlyList<MinioBucketSpec> KnownBuckets(string? defaultBucket)
    {
        var questions = string.IsNullOrWhiteSpace(defaultBucket) ? DefaultBucket : defaultBucket.Trim();
        // issue #365 (S4): tümü özel. Mevcut ortamlardaki eski (bucket geneli ya da S1 prefix) politikalar açılışta kaldırılır.
        var specs = new List<MinioBucketSpec>
        {
            new(questions, null),
            new(WorksheetsBucket, null),
            new(ExamsBucket, null),
            new(StudyPagesBucket, null),
            new(StudentAvatarsBucket, null),
        };

        // Yapılandırılmış varsayılan bucket diğerlerinden biriyle çakışırsa ilk (en kısıtlı amaçlı) tanım kazanır.
        return specs.GroupBy(s => s.Name, StringComparer.Ordinal).Select(g => g.First()).ToList();
    }

    /// <summary>
    /// Anonim <c>s3:GetObject</c> politika JSON'u; <see cref="MinioBucketSpec.AnonymousReadPrefixes"/> null/boşsa null.
    /// Liste/okuma (<c>s3:ListBucket</c>) hiçbir zaman verilmez.
    /// </summary>
    public static string? BuildAnonymousReadPolicy(MinioBucketSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.AnonymousReadPrefixes is not { Count: > 0 } prefixes)
            return null;

        var resources = prefixes
            .Select(p => $"arn:aws:s3:::{spec.Name}/{NormalizePrefix(p)}*")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var policy = new
        {
            Version = "2012-10-17",
            Statement = new[]
            {
                new
                {
                    Effect = "Allow",
                    Principal = new { AWS = new[] { "*" } },
                    Action = new[] { "s3:GetObject" },
                    Resource = resources,
                },
            },
        };
        return JsonSerializer.Serialize(policy);
    }

    private static string NormalizePrefix(string prefix)
    {
        var p = (prefix ?? string.Empty).Trim().TrimStart('/');
        if (p.Contains('*') || p.Contains('?'))
            throw new ArgumentException($"Wildcard not allowed in bucket prefix '{prefix}'.", nameof(prefix));
        return p.Length == 0 || p.EndsWith('/') ? p : p + "/";
    }
}
