using System.Security.Claims;
using BadgeService.Controllers;
using BadgeService.Entities;
using BadgeService.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BadgeService.Tests;

/// <summary>Issue #106 b (security Orta-1): rol bazlı admin bildirimleri yalnız sunucu tarafı Admin rolündeki çağırana görünür.</summary>
public class NotificationsControllerAdminBroadcastTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    private NotificationsController Controller(string sub, bool admin)
    {
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, sub));
        if (admin)
            identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        return new NotificationsController(_db.NewContext())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } }
        };
    }

    private async Task<(int Broadcast, int Own, int OtherUser, int UntypedNull)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var b = new Notification { Type = "DirectMessageReported", Title = "r", Body = "b", UserId = 0, UserKeycloakId = null };
        var own = new Notification { Type = "BadgeEarned", Title = "o", Body = "b", UserId = 1, UserKeycloakId = "admin-1" };
        var other = new Notification { Type = "BadgeEarned", Title = "x", Body = "b", UserId = 2, UserKeycloakId = "someone" };
        var untyped = new Notification { Type = "SomethingElse", Title = "u", Body = "b", UserId = 0, UserKeycloakId = null };
        ctx.Notifications.AddRange(b, own, other, untyped);
        await ctx.SaveChangesAsync();
        return (b.Id, own.Id, other.Id, untyped.Id);
    }

    private static List<int> Ids(ActionResult<IReadOnlyList<NotificationsController.NotificationDto>> r) =>
        ((IReadOnlyList<NotificationsController.NotificationDto>)((OkObjectResult)r.Result!).Value!).Select(d => d.Id).ToList();

    [Fact]
    public async Task Admin_sees_broadcast_and_own_rows_but_not_others_or_untyped_null_rows()
    {
        var s = await SeedAsync();
        var ids = Ids(await Controller("admin-1", admin: true).GetMineAsync(unreadOnly: true));
        ids.OrderBy(i => i).ShouldBe(new[] { s.Broadcast, s.Own }.OrderBy(i => i));
        ((OkObjectResult)(await Controller("admin-1", true).GetMyUnreadCountAsync(default)).Result!).Value.ShouldBe(2);
    }

    [Fact]
    public async Task NonAdmin_never_sees_or_marks_broadcast_rows()
    {
        var s = await SeedAsync();
        var c = Controller("admin-1", admin: false);
        Ids(await c.GetMineAsync()).ShouldBe(new[] { s.Own });
        ((OkObjectResult)(await Controller("admin-1", false).GetMyUnreadCountAsync(default)).Result!).Value.ShouldBe(1);
        (await Controller("admin-1", false).MarkReadAsync(s.Broadcast, default)).ShouldBeOfType<NotFoundResult>();

        await using var check = _db.NewContext();
        check.Notifications.Single(n => n.Id == s.Broadcast).IsRead.ShouldBeFalse();
    }

    [Fact]
    public async Task Admin_mark_read_on_broadcast_is_shared_and_cannot_touch_others_or_untyped_rows()
    {
        var s = await SeedAsync();
        (await Controller("admin-1", true).MarkReadAsync(s.Broadcast, default)).ShouldBeOfType<NoContentResult>();
        (await Controller("admin-1", true).MarkReadAsync(s.OtherUser, default)).ShouldBeOfType<NotFoundResult>();
        (await Controller("admin-1", true).MarkReadAsync(s.UntypedNull, default)).ShouldBeOfType<NotFoundResult>();

        // Paylaşımlı okundu: başka bir admin için de okunmuş.
        ((OkObjectResult)(await Controller("admin-2", true).GetMyUnreadCountAsync(default)).Result!).Value.ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
