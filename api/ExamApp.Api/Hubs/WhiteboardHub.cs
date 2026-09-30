using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Whiteboard;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Whiteboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Hubs;

/// <summary>Sunucu → istemci mesajları (issue #98).</summary>
public interface IWhiteboardClient
{
    /// <summary>Karşı tarafın sahneye giren (kabul edilen) elemanları.</summary>
    Task ElementsUpdated(IReadOnlyList<JsonElement> elements);

    /// <summary>Karşı tarafın imleci. <paramref name="userRole"/>: "teacher" | "student".</summary>
    Task PointerUpdated(string userRole, WhiteboardPointerDto pointer);

    /// <summary>
    /// Tahta kapandı (<see cref="WhiteboardCloseReasons"/>); istemci tahtayı salt-okunur yapmalı/kapatmalı.
    /// <c>AccessRevoked</c> yalnızca yetkisi düşen bağlantıya gider; tahta karşı taraf için açık kalabilir.
    /// </summary>
    Task BoardClosed(string reason);

    /// <summary>
    /// Karşı tarafın çevrimiçi durumu değişti. <paramref name="role"/>: "teacher" | "student". Kullanıcının tahtadaki İLK
    /// bağlantısı katılınca <c>true</c>, SON bağlantısı ayrılınca/kopunca/yetkisi düşünce <c>false</c> gönderilir.
    /// </summary>
    Task PeerPresenceChanged(string role, bool online);
}

/// <summary>
/// Ders oturumu ortak çizim tahtası (issue #98) — yol <c>/hub/whiteboard</c> (gateway üzerinden de aynı yol).
/// <para>
/// Bağlantı kapısı: JWT (WebSocket'te <c>?access_token=</c>), Teacher/Student rolü ve #287 ApprovedTeacher policy'si.
/// Her metot ayrıca üyeliği doğrular: connection → booking eşlemesini SUNUCU tutar (<see cref="IWhiteboardStore"/>),
/// istemcinin gönderdiği <c>bookingId</c> yalnızca bu eşlemeyle karşılaştırılır; pencere kapanışı her çağrıda, randevu
/// durumu + öğretmen onayı en geç <see cref="WhiteboardOptions.RevalidateSeconds"/> saniyede bir DB'den doğrulanır.
/// </para>
/// Eleman içerikleri LOGLANMAZ (yalnızca booking/kullanıcı id'leri ve sayılar).
/// </summary>
[Authorize(Roles = "Teacher,Student")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
public sealed partial class WhiteboardHub : Hub<IWhiteboardClient>
{
    public const string Path = "/hub/whiteboard";

    private readonly IWhiteboardStore _store;
    private readonly IWhiteboardAccessService _access;
    private readonly IWhiteboardSessionCloser _closer;
    private readonly IOptionsMonitor<WhiteboardOptions> _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<WhiteboardHub> _logger;

    public WhiteboardHub(IWhiteboardStore store, IWhiteboardAccessService access, IWhiteboardSessionCloser closer,
        IOptionsMonitor<WhiteboardOptions> options, TimeProvider clock, ILogger<WhiteboardHub> logger)
    {
        _store = store;
        _access = access;
        _closer = closer;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public static string GroupName(int bookingId) => $"board-{bookingId}";

    private DateTime UtcNow() => _clock.GetUtcNow().UtcDateTime;

    /// <summary>Hız sınırı anahtarı: Keycloak sub (kullanıcı başına, bağlantı sayısından bağımsız).</summary>
    private string RateKey => Context.UserIdentifier ?? Context.ConnectionId;

    /// <summary>
    /// Tahtaya katılır ve anlık sahneyi, çağıranın rolünü, pencere kapanışını ve karşı tarafın çevrimiçi olup olmadığını
    /// döner. Grup: <c>board-{bookingId}</c>. Ayrı ve daha sıkı hız sınırı (varsayılan 2/sn, patlama 4).
    /// </summary>
    public async Task<WhiteboardJoinResultDto> JoinBoard(int bookingId)
    {
        if (!_store.TryAcquireJoin(RateKey, UtcNow()))
            throw new HubException(WhiteboardErrorCodes.RateLimited);

        var access = await _access.AuthorizeAsync(Context.User!, bookingId, Context.ConnectionAborted);
        if (!access.Allowed)
        {
            _logger.LogInformation("Whiteboard join denied. BookingId={BookingId}, Reason={Reason}", bookingId, access.ErrorCode);
            throw new HubException(access.ErrorCode);
        }

        WhiteboardJoinOutcome outcome;
        try
        {
            outcome = _store.Join(Context.ConnectionId, access, Context.User!, UtcNow());
        }
        catch (WhiteboardException ex)
        {
            throw new HubException(ex.Code);
        }

        if (outcome.Previous is { } previous)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(previous.BookingId));
            await _closer.NotifyLeftAsync(previous);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(bookingId));

        // Close ile yarış: grup eklemesi sürerken tahta kapanmış (üyelik silinmiş) olabilir. Kapanış yayınını kaçırmış
        // bir bağlantıyı grupta bırakma.
        if (!_store.IsOpen(bookingId) || _store.GetMember(Context.ConnectionId)?.BookingId != bookingId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(bookingId));
            throw new HubException(WhiteboardErrorCodes.BoardClosed);
        }

        if (outcome.FirstConnectionForUser)
            await _closer.NotifyPresenceAsync(bookingId, access.UserId, access.Role, online: true);

        _logger.LogInformation("Whiteboard joined. BookingId={BookingId}, UserId={UserId}, Role={Role}, SceneElements={Count}",
            bookingId, access.UserId, access.Role, outcome.Elements.Count);

        return new WhiteboardJoinResultDto(GroupName(bookingId), outcome.Elements, outcome.ServerVersion,
            access.Role, access.WindowClosesAtUtc, outcome.PeerOnline);
    }

    /// <summary>
    /// Değişen Excalidraw elemanlarını gönderir. Sunucu <c>id</c> bazında Excalidraw kuralıyla (yüksek <c>version</c>;
    /// eşitse küçük <c>versionNonce</c>) birleştirir, yalnızca sahneye girenleri diğer katılımcıya <c>ElementsUpdated</c>
    /// ile yayınlar ve gönderenin kaybettiği id'ler için kazanan elemanları <c>corrections</c> ile geri döner.
    /// </summary>
    public async Task<WhiteboardSendResultDto> SendElements(int bookingId, JsonElement[] elements)
    {
        if (!_store.TryAcquireMessage(RateKey, UtcNow()))
            throw new HubException(WhiteboardErrorCodes.RateLimited);

        var member = await EnsureMemberAsync(bookingId);

        WhiteboardMergeResult merged;
        try
        {
            merged = _store.Merge(member.BookingId, elements ?? Array.Empty<JsonElement>());
        }
        catch (WhiteboardException ex)
        {
            _logger.LogInformation("Whiteboard elements rejected. BookingId={BookingId}, UserId={UserId}, Count={Count}, Reason={Reason}",
                member.BookingId, member.UserId, elements?.Length ?? 0, ex.Code);
            throw new HubException(ex.Code);
        }

        if (merged.Accepted.Count > 0)
            await Clients.OthersInGroup(GroupName(member.BookingId)).ElementsUpdated(merged.Accepted);

        return new WhiteboardSendResultDto(merged.ServerVersion, merged.Accepted.Count, merged.Corrections);
    }

    /// <summary>İmleç konumu. Kullanıcı başına ~20/sn; aşan güncellemeler hata vermeden düşürülür.</summary>
    public async Task SendPointer(int bookingId, WhiteboardPointerDto pointer)
    {
        if (!_store.TryAcquirePointer(RateKey, UtcNow()))
            return;

        if (pointer is null || !double.IsFinite(pointer.X) || !double.IsFinite(pointer.Y) ||
            Math.Abs(pointer.X) > MaxCoordinate || Math.Abs(pointer.Y) > MaxCoordinate ||
            (pointer.Tool is not null && !ToolPattern().IsMatch(pointer.Tool)))
            throw new HubException(WhiteboardErrorCodes.InvalidPointer);

        var member = await EnsureMemberAsync(bookingId);

        // Yalnızca doğrulanmış alanlar yeniden kurulup iletilir (istemcinin ek alanları taşınmaz).
        await Clients.OthersInGroup(GroupName(member.BookingId)).PointerUpdated(member.Role,
            new WhiteboardPointerDto { X = pointer.X, Y = pointer.Y, Tool = pointer.Tool });
    }

    /// <summary>Tahtadan ayrılır. Sahne korunur (pencere içinde yeniden katılan geri alır). Üye değilse etkisizdir.</summary>
    public async Task LeaveBoard(int bookingId)
    {
        var member = _store.GetMember(Context.ConnectionId);
        if (member is null || member.BookingId != bookingId)
            return;

        _store.Leave(Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(bookingId));
        await _closer.NotifyLeftAsync(member);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR bağlantı kopunca grup üyeliğini kendisi temizler; sahne korunur.
        if (_store.Leave(Context.ConnectionId) is { } member)
            await _closer.NotifyLeftAsync(member);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Her çağrıda: (1) bağlantı bu booking'in tahtasına kayıtlı mı (sunucu eşlemesi), (2) tahta açık mı, (3) pencere
    /// kapanmadı mı; (4) son DB doğrulaması <see cref="WhiteboardOptions.RevalidateSeconds"/>'tan eskiyse yetkiyi
    /// yeniden doğrular (randevu iptali, öğretmen askısı) — geçersizse tahtayı kapatır.
    /// </summary>
    private async Task<WhiteboardMember> EnsureMemberAsync(int bookingId)
    {
        var member = _store.GetMember(Context.ConnectionId);
        if (member is null || member.BookingId != bookingId)
            throw new HubException(WhiteboardErrorCodes.NotJoined);

        if (!_store.IsOpen(member.BookingId))
        {
            _store.Leave(Context.ConnectionId);
            throw new HubException(WhiteboardErrorCodes.BoardClosed);
        }

        var now = UtcNow();
        if (now > member.WindowClosesAtUtc)
        {
            await _closer.CloseAsync(member.BookingId, WhiteboardCloseReasons.WindowClosed, Context.ConnectionAborted);
            throw new HubException(WhiteboardErrorCodes.WindowClosed);
        }

        if (now - member.LastAuthorizedAtUtc >= TimeSpan.FromSeconds(_options.CurrentValue.RevalidateSeconds))
        {
            var access = await _access.AuthorizeAsync(Context.User!, member.BookingId, Context.ConnectionAborted);
            if (!access.Allowed)
            {
                var reason = access.ErrorCode switch
                {
                    WhiteboardErrorCodes.WindowClosed => WhiteboardCloseReasons.WindowClosed,
                    // issue #298: randevunun öğretmeni müsait değil → tahta iki taraf için kapanır (nötr neden).
                    WhiteboardErrorCodes.TeacherUnavailable => WhiteboardCloseReasons.TeacherUnavailable,
                    WhiteboardErrorCodes.BookingNotFound or WhiteboardErrorCodes.BookingNotApproved
                        => WhiteboardCloseReasons.BookingCancelled,
                    _ => null
                };

                // Tahtanın kendisi geçersizse herkes için kapat; yalnızca bu kullanıcıya özgüyse (ör. öğrencinin kendi
                // Teacher rolü) sadece bu bağlantıyı düşür.
                if (reason is not null)
                    await _closer.CloseAsync(member.BookingId, reason, Context.ConnectionAborted);
                else
                    await _closer.RevokeConnectionAsync(member, Context.ConnectionAborted);

                throw new HubException(access.ErrorCode);
            }

            _store.MarkAuthorized(Context.ConnectionId, now);
        }

        return member;
    }

    private const double MaxCoordinate = 1_000_000;

    [GeneratedRegex("^[A-Za-z0-9-]{1,16}$")]
    private static partial Regex ToolPattern();
}
