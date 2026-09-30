using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Services.Whiteboard;

public interface IWhiteboardSessionCloser
{
    /// <summary>
    /// Tahtayı kapatır: gruba <c>BoardClosed(reason)</c> yayınlar, üyeleri gruptan çıkarır ve durumu siler.
    /// Tahta zaten kapalıysa etkisizdir. Kapatıldıysa true.
    /// </summary>
    Task<bool> CloseAsync(int bookingId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Yalnızca TEK bağlantının yetkisini kaldırır: üyeliği siler, o bağlantıya <c>BoardClosed("AccessRevoked")</c>
    /// gönderir, gruptan çıkarır; kullanıcının tahtada başka bağlantısı kalmadıysa karşı tarafa çevrimdışı bildirir.
    /// </summary>
    Task RevokeConnectionAsync(WhiteboardMember member, CancellationToken ct = default);

    /// <summary>
    /// Üyeliği zaten silinmiş bir bağlantı için (LeaveBoard, kopma, tahta değiştirme): kullanıcının bu tahtada başka
    /// bağlantısı kalmadıysa karşı tarafa <c>PeerPresenceChanged(role, false)</c> gönderir.
    /// </summary>
    Task NotifyLeftAsync(WhiteboardMember member);

    /// <summary>Karşı tarafın (başka kullanıcının) bağlantılarına <c>PeerPresenceChanged(role, online)</c> gönderir.</summary>
    Task NotifyPresenceAsync(int bookingId, int userId, string role, bool online);
}

/// <summary>Singleton: hub dışından (temizlik servisi) da çağrılabilsin diye <see cref="IHubContext{THub,T}"/> kullanır.</summary>
public sealed class WhiteboardSessionCloser : IWhiteboardSessionCloser
{
    private readonly IWhiteboardStore _store;
    private readonly IHubContext<WhiteboardHub, IWhiteboardClient> _hub;
    private readonly ILogger<WhiteboardSessionCloser> _logger;

    public WhiteboardSessionCloser(IWhiteboardStore store, IHubContext<WhiteboardHub, IWhiteboardClient> hub,
        ILogger<WhiteboardSessionCloser> logger)
    {
        _store = store;
        _hub = hub;
        _logger = logger;
    }

    public async Task<bool> CloseAsync(int bookingId, string reason, CancellationToken ct = default)
    {
        // Önce durum silinir: kapanış yayını sırasında gelen SendElements "BoardClosed" alır, sahneye yazamaz.
        var connectionIds = _store.CloseBoard(bookingId);
        if (connectionIds is null)
            return false;

        var group = WhiteboardHub.GroupName(bookingId);
        await _hub.Clients.Group(group).BoardClosed(reason);
        foreach (var connectionId in connectionIds)
            await _hub.Groups.RemoveFromGroupAsync(connectionId, group, ct);

        _logger.LogInformation("Whiteboard closed. BookingId={BookingId}, Reason={Reason}, Connections={Count}",
            bookingId, reason, connectionIds.Count);
        return true;
    }

    public async Task RevokeConnectionAsync(WhiteboardMember member, CancellationToken ct = default)
    {
        // Bağlantı bu arada başka tahtaya geçtiyse o üyeliğe dokunma.
        if (_store.GetMember(member.ConnectionId)?.BookingId == member.BookingId)
            _store.Leave(member.ConnectionId);

        await _hub.Clients.Client(member.ConnectionId).BoardClosed(WhiteboardCloseReasons.AccessRevoked);
        await _hub.Groups.RemoveFromGroupAsync(member.ConnectionId, WhiteboardHub.GroupName(member.BookingId), ct);
        await NotifyLeftAsync(member);

        _logger.LogInformation("Whiteboard access revoked. BookingId={BookingId}, UserId={UserId}",
            member.BookingId, member.UserId);
    }

    public Task NotifyLeftAsync(WhiteboardMember member)
        => _store.HasUserConnection(member.BookingId, member.UserId)
            ? Task.CompletedTask
            : NotifyPresenceAsync(member.BookingId, member.UserId, member.Role, online: false);

    public Task NotifyPresenceAsync(int bookingId, int userId, string role, bool online)
    {
        var peers = _store.PeerConnectionIds(bookingId, userId);
        return peers.Count == 0 ? Task.CompletedTask : _hub.Clients.Clients(peers).PeerPresenceChanged(role, online);
    }
}
