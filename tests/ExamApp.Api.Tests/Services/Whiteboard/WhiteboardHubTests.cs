using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Hubs;
using ExamApp.Api.Models.Dtos.Whiteboard;
using ExamApp.Api.Services.Whiteboard;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using static ExamApp.Api.Tests.Services.Whiteboard.WhiteboardTestSupport;

namespace ExamApp.Api.Tests.Services.Whiteboard;

/// <summary>
/// issue #98 — hub protokolü: sunucu tarafı connection→booking eşlemesi, yayın, hız sınırı, pencere kapanışı ve
/// periyodik yeniden yetkilendirme.
/// </summary>
public class WhiteboardHubTests
{
    private const int BookingId = 7;

    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(Now));
    private readonly WhiteboardStore _store = new(Options());
    private readonly IWhiteboardAccessService _access = Substitute.For<IWhiteboardAccessService>();
    private readonly IWhiteboardSessionCloser _closer = Substitute.For<IWhiteboardSessionCloser>();
    private readonly IHubCallerClients<IWhiteboardClient> _clients = Substitute.For<IHubCallerClients<IWhiteboardClient>>();
    private readonly IWhiteboardClient _others = Substitute.For<IWhiteboardClient>();
    private readonly IGroupManager _groups = Substitute.For<IGroupManager>();

    public WhiteboardHubTests()
    {
        _clients.OthersInGroup(Arg.Any<string>()).Returns(_others);
        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), BookingId, Arg.Any<CancellationToken>())
            .Returns(Access(BookingId, closesAt: Now.AddHours(1)));
    }

    private WhiteboardHub NewHub(string connectionId = "conn-1", string sub = "kc-teacher")
    {
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(connectionId);
        context.UserIdentifier.Returns(sub);
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, sub)], "test")));
        context.ConnectionAborted.Returns(CancellationToken.None);

        return new WhiteboardHub(_store, _access, _closer, Options(), _clock, NullLogger<WhiteboardHub>.Instance)
        {
            Context = context,
            Clients = _clients,
            Groups = _groups
        };
    }

    [Fact]
    public async Task JoinBoard_denied_throws_hub_exception_with_code_and_does_not_join_group()
    {
        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), 99, Arg.Any<CancellationToken>())
            .Returns(WhiteboardAccessResult.Deny(WhiteboardErrorCodes.NotParticipant, 99));

        var ex = await Should.ThrowAsync<HubException>(() => NewHub().JoinBoard(99));

        ex.Message.ShouldBe(WhiteboardErrorCodes.NotParticipant);
        await _groups.DidNotReceiveWithAnyArgs().AddToGroupAsync(default!, default!);
        _store.GetMember("conn-1").ShouldBeNull();
    }

    [Fact]
    public async Task JoinBoard_returns_current_scene_and_adds_to_group()
    {
        await NewHub("conn-1").JoinBoard(BookingId);
        await NewHub("conn-1").SendElements(BookingId, [Element("a", 1), Element("b", 2)]);

        var result = await NewHub("conn-2", "kc-student").JoinBoard(BookingId);

        result.BoardId.ShouldBe("board-7");
        result.Elements.Count.ShouldBe(2);
        result.ServerVersion.ShouldBe(1);
        result.Role.ShouldBe(WhiteboardRoles.Teacher);
        result.WindowClosesAtUtc.ShouldBe(Now.AddHours(1));
        await _groups.Received().AddToGroupAsync("conn-2", "board-7", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendElements_without_join_is_rejected()
    {
        var ex = await Should.ThrowAsync<HubException>(() => NewHub().SendElements(BookingId, [Element("a", 1)]));

        ex.Message.ShouldBe(WhiteboardErrorCodes.NotJoined);
    }

    [Fact]
    public async Task SendElements_for_a_booking_other_than_the_joined_one_is_rejected()
    {
        await NewHub().JoinBoard(BookingId);

        // İstemcinin gönderdiği bookingId'ye güvenilmez: bağlantı 7'ye kayıtlı, 8'e yazamaz (8'in üyesi olmasa da).
        var ex = await Should.ThrowAsync<HubException>(() => NewHub().SendElements(8, [Element("a", 1)]));

        ex.Message.ShouldBe(WhiteboardErrorCodes.NotJoined);
    }

    [Fact]
    public async Task SendElements_broadcasts_only_accepted_elements_to_others()
    {
        var hub = NewHub();
        await hub.JoinBoard(BookingId);
        await hub.SendElements(BookingId, [Element("a", 3)]);
        _others.ClearReceivedCalls();

        var result = await NewHub().SendElements(BookingId, [Element("a", 2), Element("b", 1)]);

        result.Accepted.ShouldBe(1);
        result.ServerVersion.ShouldBe(2);
        result.Corrections.Single().GetProperty("version").GetInt32().ShouldBe(3); // "a" kaybetti → sunucudaki kazanan
        _clients.Received().OthersInGroup("board-7");
        await _others.Received(1).ElementsUpdated(Arg.Is<IReadOnlyList<JsonElement>>(l =>
            l.Count == 1 && l[0].GetProperty("id").GetString() == "b"));
    }

    [Fact]
    public async Task SendElements_with_nothing_new_does_not_broadcast()
    {
        await NewHub().JoinBoard(BookingId);
        await NewHub().SendElements(BookingId, [Element("a", 3)]);
        _others.ClearReceivedCalls();

        await NewHub().SendElements(BookingId, [Element("a", 3)]);

        await _others.DidNotReceiveWithAnyArgs().ElementsUpdated(default!);
    }

    [Fact]
    public async Task SendElements_image_is_rejected_with_code()
    {
        await NewHub().JoinBoard(BookingId);

        var ex = await Should.ThrowAsync<HubException>(() =>
            NewHub().SendElements(BookingId, [Element("img", 1, type: "image")]));

        ex.Message.ShouldBe(WhiteboardErrorCodes.ElementTypeNotAllowed);
        await _others.DidNotReceiveWithAnyArgs().ElementsUpdated(default!);
    }

    [Fact]
    public async Task SendElements_is_rate_limited_per_user()
    {
        await NewHub().JoinBoard(BookingId); // Join ayrı kovadan

        for (var i = 0; i < 30; i++)
            await NewHub().SendElements(BookingId, [Element("a", i + 1)]);

        var ex = await Should.ThrowAsync<HubException>(() => NewHub().SendElements(BookingId, [Element("a", 100)]));
        ex.Message.ShouldBe(WhiteboardErrorCodes.RateLimited);

        // Aynı kullanıcının ikinci bağlantısı da aynı kovayı kullanır.
        await Should.ThrowAsync<HubException>(() => NewHub("conn-9").SendElements(BookingId, [Element("a", 100)]));

        _clock.Now = _clock.Now.AddSeconds(1);
        (await NewHub().SendElements(BookingId, [Element("a", 100)])).Accepted.ShouldBe(1);
    }

    [Fact]
    public async Task JoinBoard_has_its_own_stricter_rate_limit()
    {
        for (var i = 0; i < 4; i++)
            await NewHub($"conn-{i}").JoinBoard(BookingId);

        var ex = await Should.ThrowAsync<HubException>(() => NewHub("conn-5").JoinBoard(BookingId));

        ex.Message.ShouldBe(WhiteboardErrorCodes.RateLimited);
        // Reddedilen katılım DB'ye gitmez.
        await _access.Received(4).AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), BookingId, Arg.Any<CancellationToken>());

        _clock.Now = _clock.Now.AddMilliseconds(500);
        await NewHub("conn-5").JoinBoard(BookingId);
    }

    [Fact]
    public async Task SendElements_after_window_closes_closes_the_board()
    {
        await NewHub().JoinBoard(BookingId);
        _clock.Now = new DateTimeOffset(Now.AddHours(1).AddSeconds(1));

        var ex = await Should.ThrowAsync<HubException>(() => NewHub().SendElements(BookingId, [Element("a", 1)]));

        ex.Message.ShouldBe(WhiteboardErrorCodes.WindowClosed);
        await _closer.Received(1).CloseAsync(BookingId, WhiteboardCloseReasons.WindowClosed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Membership_is_revalidated_after_interval_and_cancelled_booking_closes_board()
    {
        await NewHub().JoinBoard(BookingId);
        _access.ClearReceivedCalls();

        _clock.Now = _clock.Now.AddSeconds(10);
        await NewHub().SendElements(BookingId, [Element("a", 1)]);
        await _access.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default, default);

        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), BookingId, Arg.Any<CancellationToken>())
            .Returns(WhiteboardAccessResult.Deny(WhiteboardErrorCodes.BookingNotApproved, BookingId));
        _clock.Now = _clock.Now.AddSeconds(30);

        var ex = await Should.ThrowAsync<HubException>(() => NewHub().SendElements(BookingId, [Element("a", 2)]));

        ex.Message.ShouldBe(WhiteboardErrorCodes.BookingNotApproved);
        await _closer.Received(1).CloseAsync(BookingId, WhiteboardCloseReasons.BookingCancelled, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Suspended_teacher_on_revalidation_closes_board_with_neutral_reason()
    {
        // issue #298: randevunun öğretmeni askıda → iki tarafa da nötr TeacherUnavailable (kod + kapanış nedeni).
        await NewHub().JoinBoard(BookingId);
        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), BookingId, Arg.Any<CancellationToken>())
            .Returns(WhiteboardAccessResult.Deny(WhiteboardErrorCodes.TeacherUnavailable, BookingId));
        _clock.Now = _clock.Now.AddSeconds(31);

        var ex = await Should.ThrowAsync<HubException>(() => NewHub().SendPointer(BookingId, new WhiteboardPointerDto { X = 1, Y = 2 }));

        ex.Message.ShouldBe(WhiteboardErrorCodes.TeacherUnavailable);
        await _closer.Received(1).CloseAsync(BookingId, WhiteboardCloseReasons.TeacherUnavailable, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Callers_own_unapproved_teacher_account_only_revokes_that_connection()
    {
        // Çağıranın KENDİ Teacher hesabı onaysız (randevunun öğretmeni müsait): tahta kapanmaz, yalnızca bağlantı düşer.
        await NewHub().JoinBoard(BookingId);
        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), BookingId, Arg.Any<CancellationToken>())
            .Returns(WhiteboardAccessResult.Deny(WhiteboardErrorCodes.TeacherNotApproved, BookingId));
        _clock.Now = _clock.Now.AddSeconds(31);

        var ex = await Should.ThrowAsync<HubException>(() => NewHub().SendPointer(BookingId, new WhiteboardPointerDto { X = 1, Y = 2 }));

        ex.Message.ShouldBe(WhiteboardErrorCodes.TeacherNotApproved);
        await _closer.DidNotReceive().CloseAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _closer.Received(1).RevokeConnectionAsync(Arg.Is<WhiteboardMember>(m => m.BookingId == BookingId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendPointer_forwards_role_and_drops_updates_over_the_limit_silently()
    {
        await NewHub().JoinBoard(BookingId);

        for (var i = 0; i < 25; i++)
            await NewHub().SendPointer(BookingId, new WhiteboardPointerDto { X = i, Y = 1, Tool = "laser" });

        await _others.Received(20).PointerUpdated(WhiteboardRoles.Teacher, Arg.Any<WhiteboardPointerDto>());
    }

    [Theory]
    [InlineData(double.NaN, 0, null)]
    [InlineData(0, double.PositiveInfinity, null)]
    [InlineData(2_000_000, 0, null)]
    [InlineData(0, 0, "<script>")]
    [InlineData(0, 0, "a-very-long-tool-name")]
    public async Task SendPointer_rejects_invalid_pointer(double x, double y, string? tool)
    {
        await NewHub().JoinBoard(BookingId);

        var ex = await Should.ThrowAsync<HubException>(() =>
            NewHub().SendPointer(BookingId, new WhiteboardPointerDto { X = x, Y = y, Tool = tool }));

        ex.Message.ShouldBe(WhiteboardErrorCodes.InvalidPointer);
    }

    [Fact]
    public async Task LeaveBoard_and_disconnect_keep_the_scene()
    {
        await NewHub().JoinBoard(BookingId);
        await NewHub().SendElements(BookingId, [Element("a", 1)]);

        await NewHub().LeaveBoard(BookingId);
        _store.GetMember("conn-1").ShouldBeNull();
        await _groups.Received().RemoveFromGroupAsync("conn-1", "board-7", Arg.Any<CancellationToken>());

        _clock.Now = _clock.Now.AddSeconds(2); // yeni jetonlar
        (await NewHub("conn-3").JoinBoard(BookingId)).Elements.Count.ShouldBe(1);
        await NewHub("conn-3").OnDisconnectedAsync(null);
        _store.IsOpen(BookingId).ShouldBeTrue();
    }

    [Fact]
    public async Task JoinBoard_reports_peer_presence_and_notifies_only_on_first_connection_of_user()
    {
        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), BookingId, Arg.Any<CancellationToken>())
            .Returns(ci => ((ClaimsPrincipal)ci[0]).FindFirst(ClaimTypes.NameIdentifier)!.Value == "kc-student"
                ? Access(BookingId, 200, WhiteboardRoles.Student, Now.AddHours(1))
                : Access(BookingId, 100, WhiteboardRoles.Teacher, Now.AddHours(1)));

        var teacher = await NewHub("conn-t").JoinBoard(BookingId);
        teacher.PeerOnline.ShouldBeFalse();
        await _closer.Received(1).NotifyPresenceAsync(BookingId, 100, WhiteboardRoles.Teacher, true);

        var student = await NewHub("conn-s", "kc-student").JoinBoard(BookingId);
        student.PeerOnline.ShouldBeTrue();
        student.Role.ShouldBe(WhiteboardRoles.Student);
        await _closer.Received(1).NotifyPresenceAsync(BookingId, 200, WhiteboardRoles.Student, true);

        // Öğretmenin ikinci sekmesi: tekrar "online" bildirilmez.
        await NewHub("conn-t2").JoinBoard(BookingId);
        await _closer.Received(1).NotifyPresenceAsync(BookingId, 100, WhiteboardRoles.Teacher, true);
    }

    [Fact]
    public async Task LeaveBoard_and_disconnect_notify_presence()
    {
        await NewHub("conn-1").JoinBoard(BookingId);
        await NewHub("conn-2").JoinBoard(BookingId);

        await NewHub("conn-1").LeaveBoard(BookingId);
        await _closer.Received(1).NotifyLeftAsync(Arg.Is<WhiteboardMember>(m => m.ConnectionId == "conn-1"));

        await NewHub("conn-2").OnDisconnectedAsync(null);
        await _closer.Received(1).NotifyLeftAsync(Arg.Is<WhiteboardMember>(m => m.ConnectionId == "conn-2"));

        // Üye olmayan bağlantının kopması bildirim üretmez.
        await NewHub("conn-x").OnDisconnectedAsync(null);
        await _closer.Received(2).NotifyLeftAsync(Arg.Any<WhiteboardMember>());
    }

    [Fact]
    public async Task JoinBoard_racing_with_close_leaves_group_and_throws_BoardClosed()
    {
        // Grup eklemesi sürerken temizlik servisi tahtayı kapatır (üyelik silinir, BoardClosed yayını kaçırılır).
        _groups.AddToGroupAsync("conn-1", "board-7", Arg.Any<CancellationToken>())
            .Returns(_ => { _store.CloseBoard(BookingId); return Task.CompletedTask; });

        var ex = await Should.ThrowAsync<HubException>(() => NewHub("conn-1").JoinBoard(BookingId));

        ex.Message.ShouldBe(WhiteboardErrorCodes.BoardClosed);
        await _groups.Received(1).RemoveFromGroupAsync("conn-1", "board-7", Arg.Any<CancellationToken>());
        await _closer.DidNotReceiveWithAnyArgs().NotifyPresenceAsync(default, default, default!, default);
    }

    [Fact]
    public async Task User_specific_revalidation_failure_revokes_only_this_connection()
    {
        await NewHub().JoinBoard(BookingId);
        _access.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), BookingId, Arg.Any<CancellationToken>())
            .Returns(WhiteboardAccessResult.Deny(WhiteboardErrorCodes.UserNotResolved, BookingId));
        _clock.Now = _clock.Now.AddSeconds(31);

        var ex = await Should.ThrowAsync<HubException>(() => NewHub().SendElements(BookingId, [Element("a", 1)]));

        ex.Message.ShouldBe(WhiteboardErrorCodes.UserNotResolved);
        await _closer.Received(1).RevokeConnectionAsync(Arg.Is<WhiteboardMember>(m => m.ConnectionId == "conn-1"), Arg.Any<CancellationToken>());
        await _closer.DidNotReceiveWithAnyArgs().CloseAsync(default, default!, default);
    }
}
