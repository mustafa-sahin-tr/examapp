using System;
using ExamApp.Api.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Parents;

/// <summary>Veli bağlantısı ürün kuralları (issue #419, PO kararları — kapalı).</summary>
public static class ParentLinkRules
{
    /// <summary>Davet kodu uzunluğu (ayraçsız). Görünüm: XXXX-XXXX-XXXX.</summary>
    public const int CodeLength = 12;

    /// <summary>
    /// "İkinci veli davet kodu" geçerlilik süresi (issue #436 / epic #435 kullanıcı kararı: davet 7 gün). #419'daki 48 saat
    /// öğrenci kodu içindi; ikinci veli kodu ayrıca birincil velinin onayını gerektirir, tahmin hesap/platform sayaçlarıyla sınırlı.
    /// </summary>
    public static readonly TimeSpan CodeValidity = TimeSpan.FromDays(7);

    /// <summary>
    /// Kodla açılan bağlantı (#436: <see cref="ParentStudentLinkOrigin.InviteCode"/>) birincil veli onaylayana kadar Pending
    /// kalır; bu süre içinde onaylanmazsa düşer (sorgularda yok sayılır; süpürücü job / bir sonraki yazım Revoked'a çeker).
    /// </summary>
    public static readonly TimeSpan PendingValidity = TimeSpan.FromDays(7);

    /// <summary>
    /// Issue #436 geçiş dönemi: #419'dan kalan (<see cref="ParentStudentLinkOrigin.LegacyV1"/>) Pending istekleri öğrenci
    /// oluşturulma anından itibaren 30 gün daha onaylayabilir; sonra süpürücü job Revoked'a çeker. Yeni Pending istekler
    /// öğrenci onayına hiç düşmez.
    /// </summary>
    public static readonly TimeSpan LegacyPendingValidity = TimeSpan.FromDays(30);

    /// <summary>Pending isteğin onay süresi (kuruluş yoluna göre).</summary>
    public static TimeSpan PendingValidityFor(ParentStudentLinkOrigin origin)
        => origin == ParentStudentLinkOrigin.LegacyV1 ? LegacyPendingValidity : PendingValidity;

    /// <summary>Öğrenci başına en fazla açık (Active + süresi dolmamış Pending) veli bağlantısı.</summary>
    public const int MaxActiveParentsPerStudent = 4;

    /// <summary>
    /// Veli başına en fazla aktif çocuk — ürün kararı değil, liste uçlarını sayfalamasız sınırlı tutan güvenlik tavanı
    /// (bir veli "birden çok çocuk" bağlayabilir; 10 gerçekçi her aileyi kapsar).
    /// </summary>
    public const int MaxActiveChildrenPerParent = 10;

    /// <summary>
    /// Karışmayan alfabe: 0/O, 1/I/L hariç büyük harf + rakam (31 sembol → 12 karakterde ~2^59.4 olasılık). Deneme hesap
    /// başına dakikada 5 / günde 20 başarısız + platform devre kesicisiyle sınırlı, kod 7 gün geçerli, bağlantı ayrıca
    /// birincil velinin onayını ister (#436); hash pepper'lı olduğundan DB sızıntısında offline da denenemez (bkz. <see cref="ParentInviteCodeHasher"/>).
    /// </summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
}

/// <summary><c>ParentLinks</c> ayarları. Pepper secret'tır: appsettings'te boş; <c>ParentLinks__InviteCodePepper</c> env/user-secrets.</summary>
public sealed class ParentLinkOptions
{
    public const string SectionName = "ParentLinks";

    /// <summary>HMAC anahtarı (en az 32 karakter). Development'ta boşsa sabit dev-only değere düşülür.</summary>
    public string InviteCodePepper { get; set; } = string.Empty;
}

/// <summary>
/// Development dışında pepper'ı açılışta doğrular (<c>ValidateOnStart</c>): eksik / 32 karakterden kısa / dev-only değer →
/// uygulama başlamaz. Development'ta her zaman geçer (hasher dev-only yedeğe düşer).
/// </summary>
public sealed class ParentLinkOptionsValidator(IHostEnvironment environment) : IValidateOptions<ParentLinkOptions>
{
    public ValidateOptionsResult Validate(string? name, ParentLinkOptions options)
    {
        try
        {
            ParentInviteCodeHasher.ResolvePepper(options.InviteCodePepper ?? string.Empty, environment, logger: null);
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}

/// <summary>Davet kodu üretimi + normalize + hash (issue #419).</summary>
public interface IParentInviteCodeHasher
{
    /// <summary>Kriptografik rastgele, <see cref="ParentLinkRules.Alphabet"/>'ten <see cref="ParentLinkRules.CodeLength"/> karakter.</summary>
    string Generate();

    /// <summary>Kullanıcı girdisini kanonik biçime çevirir (büyük harf, boşluk/tire atılır). Geçersiz biçimde null.</summary>
    string? Normalize(string? input);

    /// <summary>Kanonik kodun HMAC-SHA256 hex'i (küçük harf, 64 karakter).</summary>
    string Hash(string normalizedCode);
}

/// <summary>
/// HMAC-SHA256(pepper, kod). Pepper DB'de değil — DB sızsa bile kod uzayı offline denenemez. Development dışında
/// pepper eksik/kısa ya da dev-only değerse uygulama açılmaz (<see cref="ParentLinkOptionsValidator"/>, ValidateOnStart);
/// ilk kullanımdaki aynı kontrol yalnızca savunma (DI'siz kurulan birim testleri).
/// </summary>
public sealed class ParentInviteCodeHasher : IParentInviteCodeHasher
{
    internal const int MinPepperLength = 32;
    internal const string DevFallbackPepper = "devOnlyParentInvitePepperChangeMe0123456789";
    private const string DevSecretMarker = "devOnly";

    private readonly Lazy<byte[]> _key;

    public ParentInviteCodeHasher(
        IOptions<ParentLinkOptions> options, IHostEnvironment environment, ILogger<ParentInviteCodeHasher>? logger = null)
    {
        var pepper = options.Value.InviteCodePepper ?? string.Empty;
        _key = new Lazy<byte[]>(() => Encoding.UTF8.GetBytes(ResolvePepper(pepper, environment, logger)));
    }

    public static string ResolvePepper(string pepper, IHostEnvironment environment, ILogger? logger)
    {
        if (environment.IsDevelopment())
        {
            if (pepper.Length >= MinPepperLength)
                return pepper;
            logger?.LogWarning("[ParentLinks] InviteCodePepper ayarlı değil; Development dev-only pepper kullanılıyor.");
            return DevFallbackPepper;
        }

        if (pepper.Length < MinPepperLength)
            throw new InvalidOperationException(
                "ParentLinks:InviteCodePepper ayarlanmamış veya 32 karakterden kısa. ParentLinks__InviteCodePepper ortam değişkenini ayarlayın.");
        if (pepper.Contains(DevSecretMarker, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "ParentLinks:InviteCodePepper hâlâ dev-only değer. Development dışındaki ortamlarda gerçek bir secret ayarlanmalıdır.");
        return pepper;
    }

    public string Generate() => RandomNumberGenerator.GetString(ParentLinkRules.Alphabet, ParentLinkRules.CodeLength);

    public string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 32)
            return null;

        Span<char> buffer = stackalloc char[ParentLinkRules.CodeLength];
        var count = 0;
        foreach (var raw in input)
        {
            if (raw is ' ' or '-' or '\t')
                continue;
            var c = char.ToUpperInvariant(raw);
            if (ParentLinkRules.Alphabet.IndexOf(c) < 0 || count == ParentLinkRules.CodeLength)
                return null;
            buffer[count++] = c;
        }

        return count == ParentLinkRules.CodeLength ? new string(buffer) : null;
    }

    public string Hash(string normalizedCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(normalizedCode);
        var mac = HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes(normalizedCode));
        return Convert.ToHexStringLower(mac);
    }
}
