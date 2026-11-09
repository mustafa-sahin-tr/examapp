using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary><c>--source-file</c> ile verilen yerel CSV'yi kaynak olarak açar.</summary>
public static class LocalSchoolSeedSource
{
    public static async Task<SchoolSeedSource> OpenAsync(string path, CancellationToken ct = default)
    {
        if (!File.Exists(path))
            throw new ArgumentException($"Kaynak dosya bulunamadı: '{path}'.", nameof(path));

        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return new SchoolSeedSource(path, Convert.ToHexStringLower(hash), FromCache: true, () => File.OpenRead(path));
    }
}
