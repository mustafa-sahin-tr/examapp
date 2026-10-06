using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ExamApp.Api.Services.Storage;

/// <summary>
/// issue #365 (S1): bilinen bucket ve anonim okunabilir prefix'leri.
/// <para>
/// <see cref="AnonymousReadPrefixes"/>: null → bucket tamamen özel (politika yok); boş string içeren liste → bucket'ın
/// tamamı; aksi halde yalnız verilen prefix'ler (<c>questions/</c> gibi, sonu <c>/</c>).
/// </para>
/// </summary>
public sealed record MinioBucketSpec(string Name, IReadOnlyList<string>? AnonymousReadPrefixes);

/// <summary>
/// issue #365 (S1): GEÇİCİ prefix bazlı anonim okuma politikası. Eskiden her yeni bucket'a bucket geneli
/// <c>s3:GetObject *</c> veriliyordu; varsayılan bucket'taki <c>question-transfer/*</c> (soru bankası export paketleri)
/// bu yüzden anonim indirilebiliyordu. Görseller henüz imzalı URL'ye taşınmadığı için (S2/S3) görsel prefix'leri
/// açık kalır; S4'te tüm anonim politikalar kaldırılacak.
/// </summary>
public static class MinioBucketPolicies
{
    public const string DefaultBucket = "exam-questions";
    public const string WorksheetsBucket = "worksheets";
    public const string ExamsBucket = "exams";
    public const string StudyPagesBucket = "study-pages";
    public const string StudentAvatarsBucket = "student-avatars";

    /// <summary>Varsayılan bucket'ta anonim okunabilen görsel prefix'leri (QuestionService / QuestionTransferJobRunner).</summary>
    public static readonly IReadOnlyList<string> DefaultBucketImagePrefixes = ["questions/", "answers/", "passages/"];

    private static readonly IReadOnlyList<string> WholeBucket = [""];

    /// <summary>
    /// Uygulamanın yazdığı tüm bucket'lar. <paramref name="defaultBucket"/> = <c>MinioConfig:BucketName</c>
    /// (soru/şık/paragraf görselleri + question-transfer paketleri aynı bucket'ta).
    /// </summary>
    public static IReadOnlyList<MinioBucketSpec> KnownBuckets(string? defaultBucket)
    {
        var questions = string.IsNullOrWhiteSpace(defaultBucket) ? DefaultBucket : defaultBucket.Trim();
        var specs = new List<MinioBucketSpec>
        {
            new(questions, DefaultBucketImagePrefixes),
            // Bugünkü davranış korunur (geçici, S4'te kapanır): bu bucket'larda yalnız görsel/sayfa var.
            new(WorksheetsBucket, WholeBucket),
            new(ExamsBucket, WholeBucket),
            new(StudyPagesBucket, WholeBucket),
            // URL'si hiçbir yerde saklanmıyor/sunulmuyor → tamamen özel.
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
