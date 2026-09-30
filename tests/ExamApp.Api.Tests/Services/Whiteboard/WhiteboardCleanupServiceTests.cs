using System.Security.Claims;
using ExamApp.Api.Hubs;
using ExamApp.Api.Services.Whiteboard;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static ExamApp.Api.Tests.Services.Whiteboard.WhiteboardTestSupport;

namespace ExamApp.Api.Tests.Services.Whiteboard;

/// <summary>issue #98 — temizlik servisi: penceresi dolan / iptal olan tahta kapanır, BoardClosed yayınlanır, durum silinir.</summary>
public class WhiteboardCleanupServiceTests
{
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(Now));
    private readonly WhiteboardStore _store = new(Options());
    private readonly IWhiteboardAccessService _access = Substitute.For<IWhiteboardAccessService>();
    private readonly IHubContext<WhiteboardHub, IWhiteboardClient> _hub = Substitute.For<IHubContext<WhiteboardHub, IWhiteboardClient>>();
    private readonly Dictionary<string, IWhiteboardClient> _groupClients = new();
    private readonly IGroupManager _groups = Substitute.For<IGroupManager>();
    private readonly Dictionary<string, IWhiteboardClient> _connectionClients = new();
    private readonly IWhiteboardClient _peers = Substitute.For<IWhiteboardClient>();

    public WhiteboardCleanupServiceTests()
    {
        _hub.Clients.Group(Arg.Any<string>()).Returns(ci =>
        {
            var name = ci.Arg<string>();
            if (!_groupClients.TryGetValue(name, out var client))
                _groupClients[name] = client = Substitute.For<IWhiteboardClient>();
            return client;
        });
        _hub.Groups.Returns(_groups);
        _hub.Clients.Client(Arg.Any<string>()).Returns(ci =>
        {
            var id = ci.Arg<string>();
            if (!_connectionClients.TryGetValue(id, out var client))
                _connectionClients[id] = client = Substitute.For<IWhiteboardClient>();
            return client;
        });
        _hub.Clients.Clients(Arg.Any<IReadOnlyList<string>>()).Returns(_peers);
        _access.FindInvalidBoardsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, string>());
    }

    private WhiteboardCleanupService NewService()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _access);
        var closer = new WhiteboardSessionCloser(_store, _hub, NullLogger<WhiteboardSessionCloser>.Instance);
        return new WhiteboardCleanupService(_store, closer, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            Options(), _clock, NullLogger<WhiteboardCleanupService>.Instance);
    }

    private IWhiteboardClient Group(int bookingId) => _groupClients[WhiteboardHub.GroupName(bookingId)];

    [Fact]
    public async Task Expired_board_is_closed_with_BoardClosed_and_members_removed()
    {
        _store.Join("conn-t", Access(1, closesAt: Now.AddMinutes(-1)));
        _store.Join("conn-s", Access(1, 200, WhiteboardRoles.Student, Now.AddMinutes(-1)));
        _store.Merge(1, [Element("a", 1)]);
        _store.Join("conn-live", Access(2, closesAt: Now.AddMinutes(30)));

        var closed = await NewService().SweepAsync();

        closed.ShouldBe(1);
        await Group(1).Received(1).BoardClosed(WhiteboardCloseReasons.WindowClosed);
        await _groups.Received().RemoveFromGroupAsync("conn-t", "board-1", Arg.Any<CancellationToken>());
        await _groups.Received().RemoveFromGroupAsync("conn-s", "board-1", Arg.Any<CancellationToken>());
        _store.IsOpen(1).ShouldBeFalse();
        _store.GetMember("conn-t").ShouldBeNull();
        _store.IsOpen(2).ShouldBeTrue();
        _groupClients.ContainsKey("board-2").ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelled_booking_board_is_closed_with_reason()
    {
        _store.Join("conn-t", Access(3));
        _access.FindInvalidBoardsAsync(Arg.Is<IReadOnlyCollection<int>>(ids => ids.Contains(3)), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<int, string> { [3] = WhiteboardCloseReasons.BookingCancelled });

        (await NewService().SweepAsync()).ShouldBe(1);

        await Group(3).Received(1).BoardClosed(WhiteboardCloseReasons.BookingCancelled);
        _store.IsOpen(3).ShouldBeFalse();
    }

    [Fact]
    public async Task No_open_boards_does_not_touch_the_database()
    {
        (await NewService().SweepAsync()).ShouldBe(0);

        await _access.DidNotReceiveWithAnyArgs().FindInvalidBoardsAsync(default!, default);
    }

    [Fact]
    public async Task Closing_twice_is_a_no_op()
    {
        _store.Join("conn-t", Access(4));
        var closer = new WhiteboardSessionCloser(_store, _hub, NullLogger<WhiteboardSessionCloser>.Instance);

        (await closer.CloseAsync(4, WhiteboardCloseReasons.WindowClosed)).ShouldBeTrue();
        (await closer.CloseAsync(4, WhiteboardCloseReasons.WindowClosed)).ShouldBeFalse();

        await Group(4).Received(1).BoardClosed(Arg.Any<string>());
    }

    [Fact]
    public async Task Idle_listener_is_revalidated_after_5_minutes_and_revoked_with_AccessRevoked()
    {
        _store.Join("conn-t", Access(5, 100, WhiteboardRoles.Teacher, Now.AddHours(2)));
        _store.Join("conn-s", Access(5, 200, WhiteboardRoles.Student, Now.AddHours(2)));
        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), 5, Arg.Any<CancellationToken>())
            .Returns(Access(5, 100, WhiteboardRoles.Teacher, Now.AddHours(2)));

        // 4 dk: henüz yeniden doğrulama yok.
        _clock.Now = new DateTimeOffset(Now.AddMinutes(4));
        await NewService().SweepAsync();
        await _access.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default, default);

        // Öğretmen 5. dakikadan önce çağrı yapmış (hub'da yeniden doğrulanmış) — yalnızca dinleyen öğrenci kontrol edilir.
        _store.MarkAuthorized("conn-t", _clock.Now.UtcDateTime);
        _access.AuthorizeAsync(Arg.Is<ClaimsPrincipal>(p => p == TestUser), 5, Arg.Any<CancellationToken>())
            .Returns(WhiteboardAccessResult.Deny(WhiteboardErrorCodes.NotParticipant, 5));
        _clock.Now = new DateTimeOffset(Now.AddMinutes(5));

        (await NewService().SweepAsync()).ShouldBe(0); // tahta kapanmaz

        await _access.Received(1).AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), 5, Arg.Any<CancellationToken>());
        await _connectionClients["conn-s"].Received(1).BoardClosed(WhiteboardCloseReasons.AccessRevoked);
        await _groups.Received(1).RemoveFromGroupAsync("conn-s", "board-5", Arg.Any<CancellationToken>());
        _store.GetMember("conn-s").ShouldBeNull();
        _store.GetMember("conn-t").ShouldNotBeNull();
        _store.IsOpen(5).ShouldBeTrue();
        // Öğrencinin son bağlantısı düştü → öğretmene çevrimdışı.
        await _peers.Received(1).PeerPresenceChanged(WhiteboardRoles.Student, false);
    }

    [Fact]
    public async Task Idle_listener_still_authorized_is_marked_and_not_rechecked_until_next_interval()
    {
        _store.Join("conn-s", Access(6, 200, WhiteboardRoles.Student, Now.AddHours(2)));
        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), 6, Arg.Any<CancellationToken>())
            .Returns(Access(6, 200, WhiteboardRoles.Student, Now.AddHours(2)));

        _clock.Now = new DateTimeOffset(Now.AddMinutes(5));
        await NewService().SweepAsync();
        _clock.Now = new DateTimeOffset(Now.AddMinutes(6));
        await NewService().SweepAsync();

        await _access.Received(1).AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), 6, Arg.Any<CancellationToken>());
        _store.GetMember("conn-s")!.LastAuthorizedAtUtc.ShouldBe(Now.AddMinutes(5));
    }

    [Fact]
    public async Task Transient_revalidation_error_does_not_revoke()
    {
        _store.Join("conn-s", Access(8, 200, WhiteboardRoles.Student, Now.AddHours(2)));
        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), 8, Arg.Any<CancellationToken>())
            .Returns<WhiteboardAccessResult>(_ => throw new InvalidOperationException("redis down"));

        _clock.Now = new DateTimeOffset(Now.AddMinutes(10));
        await NewService().SweepAsync();

        _store.GetMember("conn-s").ShouldNotBeNull();
        _connectionClients.ContainsKey("conn-s").ShouldBeFalse();
    }
}
