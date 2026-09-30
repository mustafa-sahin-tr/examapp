using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Whiteboard;

/// <summary>
/// Bir bağlantının sunucu tarafı tahta üyeliği (connection → booking eşlemesi). <see cref="User"/> katılım anındaki
/// principal'dır — temizlik servisi yalnızca dinleyen bağlantıların yetkisini de bununla yeniden doğrular.
/// </summary>
public sealed record WhiteboardMember(
    string ConnectionId,
    int BookingId,
    int UserId,
    string Role,
    DateTime WindowClosesAtUtc,
    DateTime LastAuthorizedAtUtc,
    ClaimsPrincipal User);

/// <summary>
/// <see cref="IWhiteboardStore.Join"/> sonucu.
/// <see cref="Previous"/>: bağlantı başka bir tahtadaysa oradaki (artık silinmiş) üyeliği.
/// <see cref="FirstConnectionForUser"/>: kullanıcının bu tahtadaki ilk bağlantısı mı (çevrimiçi bildirimi için).
/// <see cref="PeerOnline"/>: karşı tarafın (başka kullanıcı) en az bir bağlantısı var mı.
/// </summary>
public sealed record WhiteboardJoinOutcome(
    IReadOnlyList<JsonElement> Elements,
    long ServerVersion,
    WhiteboardMember? Previous,
    bool FirstConnectionForUser,
    bool PeerOnline);

/// <summary>
/// Birleştirme sonucu: sahneye giren (yayınlanacak) elemanlar, gönderenin kaybettiği id'ler için sunucudaki kazanan
/// elemanlar (<see cref="Corrections"/>) ve yeni sunucu sürümü.
/// </summary>
public sealed record WhiteboardMergeResult(
    IReadOnlyList<JsonElement> Accepted,
    IReadOnlyList<JsonElement> Corrections,
    long ServerVersion);

/// <summary>Açık tahta özeti (temizlik servisi).</summary>
public sealed record WhiteboardBoardInfo(int BookingId, DateTime WindowClosesAtUtc);

public interface IWhiteboardStore
{
    /// <summary>
    /// Bağlantıyı tahtaya kaydeder (tahta yoksa boş sahneyle açılır) ve sahnenin kopyasını döner. Bağlantı başka bir
    /// tahtadaysa oradan çıkarılır. Tahta kapanmak üzereyse (Close ile yarış) <see cref="WhiteboardException"/>
    /// (<see cref="WhiteboardErrorCodes.BoardClosed"/>) fırlatır ve üyelik kaydedilmez.
    /// </summary>
    WhiteboardJoinOutcome Join(string connectionId, WhiteboardAccessResult access, ClaimsPrincipal user, DateTime nowUtc);

    WhiteboardMember? GetMember(string connectionId);

    bool IsOpen(int bookingId);

    /// <summary>DB'den yeniden doğrulanan bağlantının zaman damgasını yeniler.</summary>
    void MarkAuthorized(string connectionId, DateTime nowUtc);

    /// <summary>
    /// Elemanları doğrular ve <c>id</c> bazında Excalidraw kuralıyla (yüksek <c>version</c>; eşitse küçük
    /// <c>versionNonce</c>) sahneye birleştirir. Herhangi bir ihlalde <see cref="WhiteboardException"/> fırlatır ve
    /// sahneye HİÇ dokunmaz (hepsi ya da hiçbiri).
    /// </summary>
    WhiteboardMergeResult Merge(int bookingId, IReadOnlyList<JsonElement> elements);

    /// <summary>Bağlantının üyeliğini kaldırır (sahne korunur). Üye değilse null.</summary>
    WhiteboardMember? Leave(string connectionId);

    /// <summary>Kullanıcının bu tahtada hâlâ bir bağlantısı var mı (çevrimdışı bildirimi için).</summary>
    bool HasUserConnection(int bookingId, int userId);

    /// <summary>Tahtadaki, <paramref name="userId"/> DIŞINDAKİ kullanıcıların bağlantıları.</summary>
    IReadOnlyList<string> PeerConnectionIds(int bookingId, int userId);

    IReadOnlyList<WhiteboardBoardInfo> ListBoards();

    IReadOnlyList<WhiteboardMember> ListMembers();

    /// <summary>Tahtayı ve tüm üyeliklerini siler; üye bağlantı id'lerini döner. Tahta yoksa null.</summary>
    IReadOnlyList<string>? CloseBoard(int bookingId);

    /// <summary>Mesaj (SendElements) token bucket'ından bir jeton almaya çalışır.</summary>
    bool TryAcquireMessage(string userKey, DateTime nowUtc);

    /// <summary>JoinBoard token bucket'ından (DB'ye giden, daha pahalı çağrı) bir jeton almaya çalışır.</summary>
    bool TryAcquireJoin(string userKey, DateTime nowUtc);

    /// <summary>İmleç token bucket'ından bir jeton almaya çalışır.</summary>
    bool TryAcquirePointer(string userKey, DateTime nowUtc);

    /// <summary>Uzun süredir kullanılmayan hız sınırı kovalarını siler.</summary>
    void PruneRateBuckets(DateTime nowUtc, TimeSpan idle);
}

/// <summary>
/// Tahta durumunun süreç içi (singleton) deposu — issue #98. Kalıcı saklama YOK (kapsam dışı): API yeniden başlarsa
/// açık tahtalar boşalır. Son katılımcı ayrılınca sahne silinmez; pencere içinde yeniden bağlanan kişi sahneyi geri alır.
/// Tahta yalnızca <see cref="CloseBoard"/> ile (pencere bitişi / randevu iptali) silinir.
/// <para>
/// ÇOKLU INSTANCE NOTU: durum ve SignalR grupları tek süreçtedir. Exam API yatay ölçeklenirse (birden fazla replika)
/// iki katılımcı farklı replikalara düşüp birbirini göremez — o gün Redis backplane (<c>AddStackExchangeRedis</c>) +
/// paylaşılan sahne deposu (Redis hash) ya da sticky session gerekir. Prod yok, kapsam dışı bırakıldı.
/// </para>
/// </summary>
public sealed class WhiteboardStore : IWhiteboardStore
{
    /// <summary>
    /// Sunucu tarafı eleman tipi izin listesi (Excalidraw). image/embeddable/iframe/magicframe vb. — dış içerik ya da
    /// dosya taşıyabilen tipler — MVP'de reddedilir.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "rectangle", "ellipse", "diamond", "line", "arrow", "freedraw", "text", "frame"
    };

    private const int MaxLinkLength = 2048;

    private readonly ConcurrentDictionary<int, Board> _boards = new();
    private readonly ConcurrentDictionary<string, WhiteboardMember> _members = new();
    private readonly ConcurrentDictionary<string, TokenBucket> _messageBuckets = new();
    private readonly ConcurrentDictionary<string, TokenBucket> _joinBuckets = new();
    private readonly ConcurrentDictionary<string, TokenBucket> _pointerBuckets = new();
    private readonly IOptionsMonitor<WhiteboardOptions> _options;

    public WhiteboardStore(IOptionsMonitor<WhiteboardOptions> options) => _options = options;

    private WhiteboardOptions Options => _options.CurrentValue;

    public WhiteboardJoinOutcome Join(string connectionId, WhiteboardAccessResult access, ClaimsPrincipal user, DateTime nowUtc)
    {
        var board = _boards.GetOrAdd(access.BookingId, _ => new Board(access.WindowClosesAtUtc));

        lock (board.Sync)
        {
            // Close ile yarış: CloseBoard tahtayı sözlükten çıkarıp kilit altında Closed'a çeker. Kapalı tahtaya üyelik
            // kaydedilmez; kilitten ÖNCE kaydedilen üyelikleri ise CloseBoard kilitten sonra zaten temizler.
            if (board.Closed)
                throw new WhiteboardException(WhiteboardErrorCodes.BoardClosed);

            WhiteboardMember? previous = null;
            if (_members.TryGetValue(connectionId, out var existing) && existing.BookingId != access.BookingId)
                previous = existing;

            var bookingMembers = _members.Values.Where(m => m.BookingId == access.BookingId && m.ConnectionId != connectionId).ToList();
            var firstForUser = !bookingMembers.Any(m => m.UserId == access.UserId);
            var peerOnline = bookingMembers.Any(m => m.UserId != access.UserId);

            _members[connectionId] = new WhiteboardMember(connectionId, access.BookingId, access.UserId, access.Role,
                access.WindowClosesAtUtc, nowUtc, user);

            board.WindowClosesAtUtc = access.WindowClosesAtUtc;
            return new WhiteboardJoinOutcome(board.Elements.Values.Select(e => e.Element).ToList(), board.ServerVersion,
                previous, firstForUser, peerOnline);
        }
    }

    public WhiteboardMember? GetMember(string connectionId)
        => _members.TryGetValue(connectionId, out var member) ? member : null;

    public bool IsOpen(int bookingId) => _boards.ContainsKey(bookingId);

    public void MarkAuthorized(string connectionId, DateTime nowUtc)
    {
        if (_members.TryGetValue(connectionId, out var member))
            _members.TryUpdate(connectionId, member with { LastAuthorizedAtUtc = nowUtc }, member);
    }

    public WhiteboardMergeResult Merge(int bookingId, IReadOnlyList<JsonElement> elements)
    {
        var options = Options;
        if (elements.Count > options.MaxElementsPerMessage)
            throw new WhiteboardException(WhiteboardErrorCodes.TooManyElementsInMessage);

        // 1) Kilit dışında doğrula + normalize et (mesaj içi aynı id → Excalidraw kuralıyla kazanan).
        var incoming = new Dictionary<string, StoredElement>(StringComparer.Ordinal);
        foreach (var raw in elements)
        {
            var element = Validate(raw, options);
            if (!incoming.TryGetValue(element.Id, out var seen) || Wins(element, seen))
                incoming[element.Id] = element;
        }

        if (!_boards.TryGetValue(bookingId, out var board))
            throw new WhiteboardException(WhiteboardErrorCodes.BoardClosed);

        lock (board.Sync)
        {
            if (board.Closed)
                throw new WhiteboardException(WhiteboardErrorCodes.BoardClosed);

            // 2) Değişiklik planı + sınır kontrolü; herhangi bir ihlalde sahneye dokunmadan reddet.
            var accepted = new List<StoredElement>();
            var corrections = new List<JsonElement>();
            var count = board.Elements.Count;
            var bytes = board.TotalBytes;
            foreach (var candidate in incoming.Values)
            {
                if (board.Elements.TryGetValue(candidate.Id, out var existing))
                {
                    // Version sıçraması: tek adımda mevcut + MaxVersionJump'ı aşamaz (sahneyi "kilitleyen" dev version'lar).
                    if ((long)candidate.Version > (long)existing.Version + options.MaxVersionJump)
                        throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);

                    if (!Wins(candidate, existing))
                    {
                        // Aynı eleman (version + nonce eşit) → yankı, düzeltme gerekmez. Aksi halde gönderen kaybetti:
                        // sunucudaki kazanan elemanı geri gönder, istemci kendini düzeltsin.
                        if (candidate.Version != existing.Version || candidate.VersionNonce != existing.VersionNonce)
                            corrections.Add(existing.Element);
                        continue;
                    }

                    bytes += candidate.Bytes - existing.Bytes;
                }
                else
                {
                    if (candidate.Version > options.MaxVersionJump)
                        throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);
                    count++;
                    bytes += candidate.Bytes;
                }

                accepted.Add(candidate);
            }

            if (count > options.MaxSceneElements)
                throw new WhiteboardException(WhiteboardErrorCodes.SceneElementLimit);
            if (bytes > options.MaxSceneBytes)
                throw new WhiteboardException(WhiteboardErrorCodes.SceneSizeLimit);

            if (accepted.Count == 0)
                return new WhiteboardMergeResult(Array.Empty<JsonElement>(), corrections, board.ServerVersion);

            // 3) Uygula.
            foreach (var element in accepted)
                board.Elements[element.Id] = element;
            board.TotalBytes = bytes;
            board.ServerVersion++;

            return new WhiteboardMergeResult(accepted.Select(e => e.Element).ToList(), corrections, board.ServerVersion);
        }
    }

    public WhiteboardMember? Leave(string connectionId)
        => _members.TryRemove(connectionId, out var member) ? member : null;

    public bool HasUserConnection(int bookingId, int userId)
        => _members.Values.Any(m => m.BookingId == bookingId && m.UserId == userId);

    public IReadOnlyList<string> PeerConnectionIds(int bookingId, int userId)
        => _members.Values.Where(m => m.BookingId == bookingId && m.UserId != userId).Select(m => m.ConnectionId).ToList();

    public IReadOnlyList<WhiteboardBoardInfo> ListBoards()
        => _boards.Select(kv => new WhiteboardBoardInfo(kv.Key, kv.Value.WindowClosesAtUtc)).ToList();

    public IReadOnlyList<WhiteboardMember> ListMembers() => _members.Values.ToList();

    public IReadOnlyList<string>? CloseBoard(int bookingId)
    {
        if (!_boards.TryRemove(bookingId, out var board))
            return null;

        lock (board.Sync)
        {
            board.Closed = true;
            board.Elements.Clear();
            board.TotalBytes = 0;
        }

        var connectionIds = new List<string>();
        foreach (var (connectionId, member) in _members)
        {
            if (member.BookingId == bookingId &&
                ((ICollection<KeyValuePair<string, WhiteboardMember>>)_members).Remove(new(connectionId, member)))
            {
                connectionIds.Add(connectionId);
            }
        }

        return connectionIds;
    }

    /// <summary>Test/teşhis: sahnedeki eleman sayısı ve takip edilen toplam bayt. Tahta yoksa null.</summary>
    public (int Count, long TotalBytes, long ComputedBytes)? SceneStats(int bookingId)
    {
        if (!_boards.TryGetValue(bookingId, out var board))
            return null;
        lock (board.Sync)
            return (board.Elements.Count, board.TotalBytes, board.Elements.Values.Sum(e => (long)e.Bytes));
    }

    public bool TryAcquireMessage(string userKey, DateTime nowUtc)
    {
        var rate = Options.MessagesPerSecond;
        return _messageBuckets.GetOrAdd(userKey, _ => new TokenBucket(rate, nowUtc)).TryTake(rate, rate, nowUtc);
    }

    public bool TryAcquireJoin(string userKey, DateTime nowUtc)
    {
        var options = Options;
        return _joinBuckets.GetOrAdd(userKey, _ => new TokenBucket(options.JoinBurst, nowUtc))
            .TryTake(options.JoinsPerSecond, options.JoinBurst, nowUtc);
    }

    public bool TryAcquirePointer(string userKey, DateTime nowUtc)
    {
        var rate = Options.PointerUpdatesPerSecond;
        return _pointerBuckets.GetOrAdd(userKey, _ => new TokenBucket(rate, nowUtc)).TryTake(rate, rate, nowUtc);
    }

    public void PruneRateBuckets(DateTime nowUtc, TimeSpan idle)
    {
        Prune(_messageBuckets);
        Prune(_joinBuckets);
        Prune(_pointerBuckets);

        void Prune(ConcurrentDictionary<string, TokenBucket> buckets)
        {
            foreach (var (key, bucket) in buckets)
            {
                if (nowUtc - bucket.LastUsedUtc > idle)
                    ((ICollection<KeyValuePair<string, TokenBucket>>)buckets).Remove(new(key, bucket));
            }
        }
    }

    /// <summary>Excalidraw uzlaşma kuralı: yüksek <c>version</c> kazanır; eşitse KÜÇÜK <c>versionNonce</c> kazanır.</summary>
    private static bool Wins(StoredElement candidate, StoredElement existing)
        => candidate.Version > existing.Version ||
           (candidate.Version == existing.Version && candidate.VersionNonce < existing.VersionNonce);

    private static StoredElement Validate(JsonElement raw, WhiteboardOptions options)
    {
        if (raw.ValueKind != JsonValueKind.Object)
            throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);

        // Tip izin listesi (image/embeddable/iframe/magicframe ve bilinmeyen tipler reddedilir).
        if (!raw.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
            throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);
        if (!AllowedTypes.Contains(type.GetString()!))
            throw new WhiteboardException(WhiteboardErrorCodes.ElementTypeNotAllowed);

        // MVP'de görsel yok: dosya referansı taşıyan her eleman reddedilir.
        if (raw.TryGetProperty("fileId", out var fileId) && fileId.ValueKind != JsonValueKind.Null)
            throw new WhiteboardException(WhiteboardErrorCodes.ImagesNotSupported);

        // customData: istemciye özgü serbest yük — karşı tarafa taşınmasına izin verilmez.
        if (raw.TryGetProperty("customData", out _))
            throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);

        // link: yalnızca http/https (javascript:, data: vb. karşı tarafta tıklanınca çalışabilir).
        if (raw.TryGetProperty("link", out var link) && link.ValueKind != JsonValueKind.Null)
        {
            if (link.ValueKind != JsonValueKind.String)
                throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);
            var url = link.GetString()!;
            if (url.Length > MaxLinkLength ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);
        }

        if (!raw.TryGetProperty("id", out var idProp) || idProp.ValueKind != JsonValueKind.String)
            throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);
        var id = idProp.GetString();
        if (string.IsNullOrEmpty(id) || id.Length > options.MaxElementIdLength)
            throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);

        if (!raw.TryGetProperty("version", out var versionProp) ||
            versionProp.ValueKind != JsonValueKind.Number ||
            !versionProp.TryGetInt32(out var version) || version < 0)
            throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);

        // versionNonce: Excalidraw her elemanda gönderir; yoksa 0 sayılır, varsa int olmalı.
        var versionNonce = 0;
        if (raw.TryGetProperty("versionNonce", out var nonceProp) &&
            (nonceProp.ValueKind != JsonValueKind.Number || !nonceProp.TryGetInt32(out versionNonce)))
            throw new WhiteboardException(WhiteboardErrorCodes.InvalidElement);

        // Clone: elemanı mesajın JsonDocument'ından koparır (sahnede uzun süre yaşar).
        var element = raw.Clone();
        return new StoredElement(id, version, versionNonce, element, Encoding.UTF8.GetByteCount(element.GetRawText()));
    }

    private sealed record StoredElement(string Id, int Version, int VersionNonce, JsonElement Element, int Bytes);

    private sealed class Board(DateTime windowClosesAtUtc)
    {
        public object Sync { get; } = new();
        public Dictionary<string, StoredElement> Elements { get; } = new(StringComparer.Ordinal);
        public long TotalBytes { get; set; }
        public long ServerVersion { get; set; }
        public bool Closed { get; set; }
        public DateTime WindowClosesAtUtc { get; set; } = windowClosesAtUtc;
    }

    /// <summary>Basit token bucket: <c>capacity</c> kadar patlama, saniyede <c>rate</c> jeton sürekli dolum.</summary>
    private sealed class TokenBucket(int capacity, DateTime nowUtc)
    {
        private readonly object _sync = new();
        private double _tokens = capacity;
        private DateTime _lastRefillUtc = nowUtc;

        public DateTime LastUsedUtc { get; private set; } = nowUtc;

        public bool TryTake(int ratePerSecond, int maxTokens, DateTime nowUtc)
        {
            lock (_sync)
            {
                var elapsed = (nowUtc - _lastRefillUtc).TotalSeconds;
                if (elapsed > 0)
                {
                    _tokens = Math.Min(maxTokens, _tokens + elapsed * ratePerSecond);
                    _lastRefillUtc = nowUtc;
                }

                LastUsedUtc = nowUtc;
                if (_tokens < 1)
                    return false;

                _tokens -= 1;
                return true;
            }
        }
    }
}
