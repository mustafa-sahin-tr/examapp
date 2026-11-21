using System.Net;
using ExamApp.Api.Services.Video;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Shouldly;
using Xunit;

namespace ExamApp.Api.Tests.Services.Video;

/// <summary>Issue #490 — booking rooms are destroyed server-side through prosody's mod_muc_end_meeting.</summary>
public class JitsiVideoRoomCloserTests
{
    private static readonly string AppSecret = new('a', 32);
    private static readonly string RoomSecret = new('b', 16);
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static JitsiVideoRoomCloser Create(RecordingHandler handler, string internalUrl = "http://prosody:5280")
    {
        var options = new VideoOptions
        {
            Jitsi = new JitsiOptions
            {
                AppId = "examapp",
                AppSecret = AppSecret,
                RoomSecret = RoomSecret,
                XmppDomain = "meet.jitsi",
                MucDomain = "muc.meet.jitsi",
                InternalHttpUrl = internalUrl
            }
        };
        return new JitsiVideoRoomCloser(new HttpClient(handler), Options.Create(options), new FixedClock(Now),
            NullLogger<JitsiVideoRoomCloser>.Instance);
    }

    [Fact]
    public async Task CloseRoomAsync_PostsEndMeetingForTheBookingRoomWithSignedToken()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var closed = await Create(handler, "http://prosody:5280/").CloseRoomAsync(42);

        closed.ShouldBeTrue();
        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        var room = JitsiVideoSessionProvider.BuildRoomName(42, RoomSecret);
        request.RequestUri!.GetLeftPart(UriPartial.Path).ShouldBe("http://prosody:5280/end-meeting");
        Uri.UnescapeDataString(request.RequestUri.Query).ShouldBe($"?conference={room}@muc.meet.jitsi");
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(request.Headers.Authorization.Parameter,
            new TokenValidationParameters
            {
                ValidIssuer = "examapp",
                ValidAudience = "jitsi",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AppSecret)),
                ValidateLifetime = false
            });
        validation.IsValid.ShouldBeTrue();
        validation.Claims["sub"].ShouldBe("meet.jitsi");
        validation.Claims["room"].ShouldBe(room);
        // Short-lived: a leaked close token is useless within minutes.
        ((JsonWebToken)validation.SecurityToken).ValidTo.ShouldBeLessThanOrEqualTo(Now.UtcDateTime.AddMinutes(2));
    }

    [Fact]
    public async Task CloseRoomAsync_RoomNotOpen_ReturnsFalse()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await Create(handler).CloseRoomAsync(42)).ShouldBeFalse();
    }

    [Fact]
    public async Task CloseRoomAsync_Rejected_ReturnsFalseWithoutThrowing()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        (await Create(handler).CloseRoomAsync(42)).ShouldBeFalse();
    }

    [Fact]
    public async Task CloseRoomAsync_ProsodyUnreachable_ReturnsFalseWithoutThrowing()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection refused"));

        (await Create(handler).CloseRoomAsync(42)).ShouldBeFalse();
    }

    [Fact]
    public async Task CloseRoomAsync_NotConfigured_DoesNotCallProsody()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        (await Create(handler, internalUrl: "").CloseRoomAsync(42)).ShouldBeFalse();
        handler.Requests.ShouldBeEmpty();
    }
}
