using ExamApp.Api.Services.Video;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Xunit;
using NSubstitute;

namespace ExamApp.Api.Tests.Services.Video;

/// <summary>
/// Issue #97 — Self-host Jitsi video provider: room name determinism, JWT token generation,
/// moderator affiliation, token expiration, and secret validation.
/// </summary>
public class JitsiVideoSessionProviderTests
{
    private const string TestAppId = "testapp";
    private const string TestXmppDomain = "meet.test";
    private const string TestPublicBaseUrl = "http://localhost:8000";
    private const int TestTokenLifetimeMinutes = 180;

    private JitsiVideoSessionProvider CreateProvider(TimeProvider? timeProvider = null, IHostEnvironment? hostEnv = null)
    {
        var jitsi = new JitsiOptions();
        jitsi.AppSecret = new string('a', 32);
        jitsi.RoomSecret = new string('b', 16);
        jitsi.AppId = TestAppId;
        jitsi.XmppDomain = TestXmppDomain;
        jitsi.PublicBaseUrl = TestPublicBaseUrl;
        jitsi.TokenLifetimeMinutes = TestTokenLifetimeMinutes;

        var options = new VideoOptions { Jitsi = jitsi };
        var env = hostEnv ?? CreateFakeHostEnvironment(Environments.Development);
        return new JitsiVideoSessionProvider(
            Options.Create(options),
            timeProvider ?? TimeProvider.System,
            env);
    }

    private IHostEnvironment CreateFakeHostEnvironment(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        return env;
    }

    // ------ Room name determinism ------

    [Fact]
    public async Task CreateOrJoinSessionAsync_RoomName_IsDeterministic()
    {
        const int bookingId = 42;
        var provider = CreateProvider();
        var endUtc = DateTime.UtcNow.AddMinutes(50);
        var request = new VideoSessionRequest(
            BookingId: bookingId,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: endUtc,
            WindowClosesAtUtc: endUtc.AddMinutes(30));

        var session1 = await provider.CreateOrJoinSessionAsync(request);
        var session2 = await provider.CreateOrJoinSessionAsync(request);

        Assert.Equal(session1.RoomName, session2.RoomName);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_RoomName_VariesByBookingId()
    {
        var provider = CreateProvider();
        var end1 = DateTime.UtcNow.AddMinutes(50);
        var request1 = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: end1,
            WindowClosesAtUtc: end1.AddMinutes(30));

        var end2 = DateTime.UtcNow.AddMinutes(50);
        var request2 = new VideoSessionRequest(
            BookingId: 2,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: end2,
            WindowClosesAtUtc: end2.AddMinutes(30));

        var session1 = await provider.CreateOrJoinSessionAsync(request1);
        var session2 = await provider.CreateOrJoinSessionAsync(request2);

        Assert.NotEqual(session1.RoomName, session2.RoomName);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_RoomName_Format_StartsWithBookingPrefix()
    {
        const int bookingId = 42;
        var provider = CreateProvider();
        var endUtc = DateTime.UtcNow.AddMinutes(50);
        var request = new VideoSessionRequest(
            BookingId: bookingId,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: endUtc,
            WindowClosesAtUtc: endUtc.AddMinutes(30));

        var session = await provider.CreateOrJoinSessionAsync(request);

        Assert.StartsWith($"booking-{bookingId}-", session.RoomName);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_RoomName_Format_ContainsOnlyLowercaseHexAndDashes()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 999,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        // Format: booking-{id}-{12 hex chars}
        Assert.Matches(@"^booking-\d+-[a-f0-9]{12}$", session.RoomName);
    }

    // ------ JWT token generation ------

    [Fact]
    public async Task CreateOrJoinSessionAsync_TeacherRole_TokenHasOwnerAffiliation()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "John Teacher",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        var userContext = GetContextUser(session.Token);
        Assert.Equal("owner", userContext["affiliation"]);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_StudentRole_TokenHasMemberAffiliation()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 200,
            ParticipantDisplayName: "Jane Student",
            ParticipantRole: VideoParticipantRoles.Student,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        var userContext = GetContextUser(session.Token);
        Assert.Equal("member", userContext["affiliation"]);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TokenClaims_HasCorrectIssuer()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        var token = ParseToken(session.Token);
        var claimDict = token.Claims.ToDictionary(c => c.Type, c => c.Value);
        Assert.Equal(TestAppId, claimDict["iss"]);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TokenClaims_HasJitsiAudience()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        var token = ParseToken(session.Token);
        var claimDict = token.Claims.ToDictionary(c => c.Type, c => (object)c.Value);
        Assert.Equal("jitsi", claimDict["aud"]);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TokenClaims_HasXmppDomainAsSubject()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        var token = ParseToken(session.Token);
        var claimDict = token.Claims.ToDictionary(c => c.Type, c => (object)c.Value);
        Assert.Equal(TestXmppDomain, claimDict["sub"]);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TokenClaims_HasRoomName()
    {
        var provider = CreateProvider();
        var bookingId = 42;
        var endUtc = DateTime.UtcNow.AddMinutes(50);
        var request = new VideoSessionRequest(
            BookingId: bookingId,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: endUtc,
            WindowClosesAtUtc: endUtc.AddMinutes(30));

        var session = await provider.CreateOrJoinSessionAsync(request);

        var token = ParseToken(session.Token);
        var claimDict = token.Claims.ToDictionary(c => c.Type, c => (object)c.Value);
        var roomName = claimDict["room"].ToString();
        Assert.StartsWith($"booking-{bookingId}-", roomName);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TokenClaims_HasUserIdInContext()
    {
        var provider = CreateProvider();
        var userId = 12345;
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: userId,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        var userContext = GetContextUser(session.Token);
        Assert.Equal(userId.ToString(), userContext["id"]);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TokenClaims_HasUserNameInContext()
    {
        var provider = CreateProvider();
        var displayName = "Alice Wonderland";
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: displayName,
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        var userContext = GetContextUser(session.Token);
        Assert.Equal(displayName, userContext["name"]);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TokenExpiration_WhenWindowBeforeLifetime_ExpiresAtWindow()
    {
        var fixedNow = new DateTime(2025, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedNow);
        var provider = CreateProvider(timeProvider);

        // Window closes at 10:30 (30 min after now), lifetime is 180 min → exp = window (Min logic)
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: fixedNow.AddMinutes(-10),
            EndUtc: fixedNow.AddMinutes(15),
            WindowClosesAtUtc: fixedNow.AddMinutes(30));

        var session = await provider.CreateOrJoinSessionAsync(request);

        // Should be capped at WindowClosesAtUtc (shorter than lifetime)
        var expectedExpire = fixedNow.AddMinutes(30);
        Assert.Equal(expectedExpire, session.ExpiresAt);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TokenExpiration_WhenWindowAfterLifetime_ExpiresAtLifetime()
    {
        var fixedNow = new DateTime(2025, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedNow);
        var provider = CreateProvider(timeProvider);

        // Window closes at 11:30 (90 min after now), lifetime is 60 min (default) → exp = lifetime
        // Need to override lifetime to 60 for this test
        var jitsi = new JitsiOptions();
        jitsi.AppSecret = new string('a', 32);
        jitsi.RoomSecret = new string('b', 16);
        jitsi.AppId = TestAppId;
        jitsi.XmppDomain = TestXmppDomain;
        jitsi.PublicBaseUrl = TestPublicBaseUrl;
        jitsi.TokenLifetimeMinutes = 60; // Short lifetime

        var options = new VideoOptions { Jitsi = jitsi };
        var env = CreateFakeHostEnvironment(Environments.Development);
        var providerShortLife = new JitsiVideoSessionProvider(
            Options.Create(options),
            timeProvider,
            env);

        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: fixedNow.AddMinutes(-10),
            EndUtc: fixedNow.AddMinutes(50),
            WindowClosesAtUtc: fixedNow.AddMinutes(90));

        var session = await providerShortLife.CreateOrJoinSessionAsync(request);

        // exp = Min(now + 60, windowCloses at 90) = now + 60
        var expectedExpire = fixedNow.AddMinutes(60);
        Assert.Equal(expectedExpire, session.ExpiresAt);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TokenClaims_NotBeforeIsEarlyBuffer()
    {
        var fixedNow = new DateTime(2025, 9, 11, 10, 0, 0, DateTimeKind.Utc);
        var timeProvider = new FakeTimeProvider(fixedNow);
        var provider = CreateProvider(timeProvider);

        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        var token = ParseToken(session.Token);
        var claimDict = token.Claims.ToDictionary(c => c.Type, c => (object)c.Value);
        var nbfUnix = long.Parse(claimDict["nbf"].ToString()!);
        var nbfDateTime = UnixTimeStampToDateTime(nbfUnix);

        // nbf should be ~5 minutes before now (to account for clock skew)
        var expectedNbf = fixedNow.AddMinutes(-5);
        Assert.True(Math.Abs((nbfDateTime - expectedNbf).TotalSeconds) < 2);
    }

    // ------ Return value validation ------

    [Fact]
    public async Task CreateOrJoinSessionAsync_Returns_CorrectProvider()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        Assert.Equal("Jitsi", session.Provider);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_Returns_CorrectDomain()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        Assert.Equal("localhost:8000", session.Domain);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_Returns_CorrectBaseUrl()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        Assert.Equal(TestPublicBaseUrl, session.BaseUrl);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_Returns_JoinUrlWithRoomAndToken()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        Assert.StartsWith(TestPublicBaseUrl, session.JoinUrl);
        Assert.Contains("jwt=", session.JoinUrl);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_TeacherRole_IsModerator()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        Assert.True(session.IsModerator);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_StudentRole_IsNotModerator()
    {
        var provider = CreateProvider();
        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Student,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        Assert.False(session.IsModerator);
    }

    // ------ Secret validation ------

    [Fact]
    public async Task CreateOrJoinSessionAsync_AppSecretTooShort_ThrowsInvalidOperation()
    {
        var jitsi = new JitsiOptions();
        jitsi.AppSecret = "short"; // < 32 chars
        jitsi.RoomSecret = new string('b', 16);
        jitsi.AppId = TestAppId;
        jitsi.XmppDomain = TestXmppDomain;
        jitsi.PublicBaseUrl = TestPublicBaseUrl;
        jitsi.TokenLifetimeMinutes = TestTokenLifetimeMinutes;

        var options = new VideoOptions { Jitsi = jitsi };
        var env = CreateFakeHostEnvironment(Environments.Development);
        var provider = new JitsiVideoSessionProvider(Options.Create(options), TimeProvider.System, env);

        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CreateOrJoinSessionAsync(request));

        Assert.Contains("AppSecret", ex.Message);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_RoomSecretTooShort_ThrowsInvalidOperation()
    {
        var jitsi = new JitsiOptions();
        jitsi.AppSecret = new string('a', 32);
        jitsi.RoomSecret = "short"; // < 16 chars
        jitsi.AppId = TestAppId;
        jitsi.XmppDomain = TestXmppDomain;
        jitsi.PublicBaseUrl = TestPublicBaseUrl;
        jitsi.TokenLifetimeMinutes = TestTokenLifetimeMinutes;

        var options = new VideoOptions { Jitsi = jitsi };
        var env = CreateFakeHostEnvironment(Environments.Development);
        var provider = new JitsiVideoSessionProvider(Options.Create(options), TimeProvider.System, env);

        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CreateOrJoinSessionAsync(request));

        Assert.Contains("RoomSecret", ex.Message);
    }

    // ------ Environment-specific validation ------

    [Fact]
    public async Task CreateOrJoinSessionAsync_ProductionWithChangeMeAppSecret_ThrowsInvalidOperation()
    {
        var jitsi = new JitsiOptions();
        jitsi.AppSecret = "ChangeMe_placeholder_32_characters";
        jitsi.RoomSecret = new string('b', 16);
        jitsi.AppId = TestAppId;
        jitsi.XmppDomain = TestXmppDomain;
        jitsi.PublicBaseUrl = TestPublicBaseUrl;
        jitsi.TokenLifetimeMinutes = TestTokenLifetimeMinutes;

        var options = new VideoOptions { Jitsi = jitsi };
        var env = CreateFakeHostEnvironment(Environments.Production);
        var provider = new JitsiVideoSessionProvider(Options.Create(options), TimeProvider.System, env);

        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CreateOrJoinSessionAsync(request));

        Assert.Contains("ChangeMe", ex.Message);
    }

    [Fact]
    public async Task CreateOrJoinSessionAsync_DevelopmentWithChangeMeAppSecret_Succeeds()
    {
        var jitsi = new JitsiOptions();
        jitsi.AppSecret = "ChangeMe_placeholder_32_characters";
        jitsi.RoomSecret = new string('b', 16);
        jitsi.AppId = TestAppId;
        jitsi.XmppDomain = TestXmppDomain;
        jitsi.PublicBaseUrl = TestPublicBaseUrl;
        jitsi.TokenLifetimeMinutes = TestTokenLifetimeMinutes;

        var options = new VideoOptions { Jitsi = jitsi };
        var env = CreateFakeHostEnvironment(Environments.Development);
        var provider = new JitsiVideoSessionProvider(Options.Create(options), TimeProvider.System, env);

        var request = new VideoSessionRequest(
            BookingId: 1,
            ParticipantUserId: 100,
            ParticipantDisplayName: "Test User",
            ParticipantRole: VideoParticipantRoles.Teacher,
            StartUtc: DateTime.UtcNow.AddMinutes(-10),
            EndUtc: DateTime.UtcNow.AddMinutes(50),
            WindowClosesAtUtc: DateTime.UtcNow.AddMinutes(80));

        var session = await provider.CreateOrJoinSessionAsync(request);

        Assert.NotNull(session);
        Assert.Equal("Jitsi", session.Provider);
    }

    // ------ Helpers ------

    private JsonWebToken ParseToken(string token)
    {
        var handler = new JsonWebTokenHandler();
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(new string('a', 32)));

        var result = handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ClockSkew = TimeSpan.Zero
        }).GetAwaiter().GetResult();

        return result.SecurityToken as JsonWebToken ?? throw new InvalidOperationException("Could not parse token");
    }

    private Dictionary<string, object> GetContextUser(string tokenString)
    {
        // Use JwtSecurityTokenHandler to read the payload directly
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenString);

        // The payload should contain a "context" claim
        if (!jwtToken.Payload.TryGetValue("context", out var contextObj))
        {
            throw new InvalidOperationException("Context claim not found in token payload");
        }

        // The context might be a JsonElement or Dictionary depending on how it was serialized
        if (contextObj is JsonElement jsonElement)
        {
            if (jsonElement.TryGetProperty("user", out var userElement))
            {
                var userDict = new Dictionary<string, object>();
                foreach (var prop in userElement.EnumerateObject())
                {
                    userDict[prop.Name] = prop.Value.GetString() ?? prop.Value.ToString();
                }
                return userDict;
            }
        }
        else if (contextObj is Dictionary<string, object> contextDict)
        {
            if (contextDict.TryGetValue("user", out var userClaim))
            {
                return userClaim as Dictionary<string, object> ?? throw new InvalidOperationException("User claim not found");
            }
        }

        throw new InvalidOperationException("User not found in context claim");
    }

    private DateTime UnixTimeStampToDateTime(long unixTimeStamp)
    {
        var dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        dateTime = dateTime.AddSeconds(unixTimeStamp);
        return dateTime;
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTime _fixedNow;

        public FakeTimeProvider(DateTime fixedNow)
        {
            _fixedNow = fixedNow;
        }

        public override DateTimeOffset GetUtcNow() => new(_fixedNow);
    }
}
