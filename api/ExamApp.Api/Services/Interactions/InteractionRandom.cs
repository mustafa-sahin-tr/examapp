using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// review #576 (H1/M1): kayıtta tanım karıştırma (kart / sağ öğe sırası ve opak id'ler) için rastgelelik kaynağı. Üretimde
/// <see cref="CryptoInteractionRandom"/> (<see cref="RandomNumberGenerator"/>); testlerde tohumlu bir uygulama verilebilir.
/// </summary>
public interface IInteractionRandom
{
    /// <summary>[0, <paramref name="maxExclusive"/>) aralığında düzgün dağılmış tam sayı.</summary>
    int Next(int maxExclusive);
}

/// <summary>Kriptografik rastgelelik (<see cref="RandomNumberGenerator.GetInt32(int)"/>); iş parçacığı güvenli.</summary>
public sealed class CryptoInteractionRandom : IInteractionRandom
{
    public static CryptoInteractionRandom Instance { get; } = new();

    public int Next(int maxExclusive) => RandomNumberGenerator.GetInt32(maxExclusive);
}

/// <summary>review #576 (H1/M1): karıştırma yardımcıları — Fisher–Yates ve opak id üretimi; JSON yazımı.</summary>
public static class InteractionScramble
{
    public const int OpaqueIdLength = 8;
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";

    // Türkçe harfler kaçışsız yazılsın (kayıtlı plan okunur kalsın); HTML'e duyarlı karakterler yine kaçışlı.
    private static readonly JsonSerializerOptions WriteOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public static void Shuffle<T>(IList<T> items, IInteractionRandom random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>8 karakterlik [a-z0-9] id; <paramref name="taken"/> içinde yoksa eklenip döner (<see cref="InteractionIds"/> kuralına uyar).</summary>
    public static string NewOpaqueId(HashSet<string> taken, IInteractionRandom random)
    {
        while (true)
        {
            var chars = new char[OpaqueIdLength];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = Alphabet[random.Next(Alphabet.Length)];
            var id = new string(chars);
            if (InteractionIds.IsValid(id) && taken.Add(id))
                return id;
        }
    }

    public static string Write(JsonNode node) => node.ToJsonString(WriteOptions);
}
