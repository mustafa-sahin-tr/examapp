using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Services.Whiteboard;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services.Whiteboard;

internal static class WhiteboardTestSupport
{
    public static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    public static IOptionsMonitor<WhiteboardOptions> Options(Action<WhiteboardOptions>? configure = null)
    {
        var options = new WhiteboardOptions();
        configure?.Invoke(options);
        var monitor = Substitute.For<IOptionsMonitor<WhiteboardOptions>>();
        monitor.CurrentValue.Returns(options);
        return monitor;
    }

    public static readonly ClaimsPrincipal TestUser =
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "kc-test")], "test"));

    /// <summary>Excalidraw benzeri minimal eleman (sunucu id/version/versionNonce/type/fileId/link/customData'ya bakar).</summary>
    public static JsonElement Element(string id, int version, string type = "rectangle", string? extra = null, int nonce = 0)
    {
        var json = $"{{\"id\":\"{id}\",\"version\":{version},\"versionNonce\":{nonce},\"type\":\"{type}\",\"x\":10,\"y\":20{extra}}}";
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    public static WhiteboardJoinOutcome Join(this WhiteboardStore store, string connectionId, WhiteboardAccessResult access)
        => store.Join(connectionId, access, TestUser, Now);

    public static JsonElement Raw(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static WhiteboardAccessResult Access(int bookingId, int userId = 100, string role = WhiteboardRoles.Teacher,
        DateTime? closesAt = null)
        => new(null, bookingId, userId, role, closesAt ?? Now.AddHours(1));
}
