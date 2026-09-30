using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Whiteboard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #98 — ortak çizim tahtası uçtan uca: gerçek pipeline (auth, ApprovedTeacher policy, hub, EF/Postgres) üzerinde
/// iki SignalR istemcisi (TestServer, LongPolling). Öğretmen çizer → öğrenci alır; yabancı reddedilir; yeniden katılan
/// sahneyi geri alır; randevu iptal olunca temizlik turu BoardClosed yayınlar.
/// </summary>
public class WhiteboardHubEndpointsTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int TeacherUserId = 980_001, StudentUserId = 980_002, StrangerUserId = 980_003;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private async Task SeedProfileAsync(int userId, string sub, string role)
    {
        var cache = Factory.Services.GetRequiredService<IDistributedCache>();
        await cache.SetStringAsync(sub, JsonSerializer.Serialize(new UserProfileDto
        {
            Id = userId, KeycloakId = sub, Role = role, FullName = "Test User", Email = "t@t.local"
        }));
    }

    /// <summary>Katılım penceresi içinde (şimdi − 10 dk başlayan) onaylı booking; gece yarısını geçmez.</summary>
    private Task<int> SeedBookingAsync() => WithDbAsync(async db =>
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc).AddMinutes(-10);
        if (start.Date != now.Date)
            start = now.Date;
        var startTime = TimeOnly.FromDateTime(start);
        var endTime = startTime.AddHours(1) < startTime ? new TimeOnly(23, 59) : startTime.AddHours(1);

        var teacher = new Teacher { UserId = TeacherUserId, AccountApprovedAt = DateTime.UtcNow, Bio = "t" };
        var student = new Student { UserId = StudentUserId, StudentNumber = "WB-1" };
        var stranger = new Student { UserId = StrangerUserId, StudentNumber = "WB-2" };
        db.AddRange(teacher, student, stranger);
        await db.SaveChangesAsync();

        var slot = new TeacherAvailabilitySlot
        {
            TeacherId = teacher.Id, Date = DateOnly.FromDateTime(now), StartTime = startTime, EndTime = endTime,
            CreatedAt = DateTime.UtcNow
        };
        var booking = new Booking
        {
            TeacherId = teacher.Id, StudentId = student.Id, AvailabilitySlot = slot, Status = BookingStatus.Approved,
            CreatedAt = DateTime.UtcNow, DecisionAt = DateTime.UtcNow
        };
        db.Add(booking);
        await db.SaveChangesAsync();
        return booking.Id;
    });

    private HubConnection Connect(string sub, string roles) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "hub/whiteboard"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                o.Headers["X-Test-Auth"] = sub;
                o.Headers["X-Test-Username"] = sub;
                o.Headers["X-Test-Roles"] = roles;
            })
            .Build();

    private static JsonElement Element(string id, int version) =>
        JsonDocument.Parse($"{{\"id\":\"{id}\",\"version\":{version},\"type\":\"freedraw\",\"points\":[[0,0],[5,5]]}}")
            .RootElement.Clone();

    [Fact]
    public async Task Teacher_draws_student_receives_stranger_rejected_and_cancel_closes_board()
    {
        await SeedProfileAsync(TeacherUserId, "kc-wb-teacher", "Teacher");
        await SeedProfileAsync(StudentUserId, "kc-wb-student", "Student");
        await SeedProfileAsync(StrangerUserId, "kc-wb-stranger", "Student");
        var bookingId = await SeedBookingAsync();

        await using var teacher = Connect("kc-wb-teacher", "Teacher");
        await using var student = Connect("kc-wb-student", "Student");
        await using var stranger = Connect("kc-wb-stranger", "Student");

        var received = new TaskCompletionSource<JsonElement[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        student.On<JsonElement[]>("ElementsUpdated", e => received.TrySetResult(e));
        var studentClosed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        student.On<string>("BoardClosed", r => studentClosed.TrySetResult(r));
        var teacherGotOwn = false;
        teacher.On<JsonElement[]>("ElementsUpdated", _ => teacherGotOwn = true);
        var studentOnline = new TaskCompletionSource<(string, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var studentOffline = new TaskCompletionSource<(string, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        teacher.On<string, bool>("PeerPresenceChanged", (role, online) =>
            (online ? studentOnline : studentOffline).TrySetResult((role, online)));

        await teacher.StartAsync();
        await student.StartAsync();
        await stranger.StartAsync();

        var teacherJoin = await teacher.InvokeAsync<JsonElement>("JoinBoard", bookingId);
        teacherJoin.GetProperty("boardId").GetString().ShouldBe($"board-{bookingId}");
        teacherJoin.GetProperty("elements").GetArrayLength().ShouldBe(0);
        teacherJoin.GetProperty("role").GetString().ShouldBe("teacher");
        teacherJoin.GetProperty("peerOnline").GetBoolean().ShouldBeFalse();
        teacherJoin.GetProperty("windowClosesAtUtc").GetDateTime().ShouldBeGreaterThan(DateTime.UtcNow);

        var studentJoin = await student.InvokeAsync<JsonElement>("JoinBoard", bookingId);
        studentJoin.GetProperty("role").GetString().ShouldBe("student");
        studentJoin.GetProperty("peerOnline").GetBoolean().ShouldBeTrue();
        (await studentOnline.Task.WaitAsync(Timeout)).ShouldBe(("student", true));

        var strangerError = await Should.ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>(
            () => stranger.InvokeAsync<JsonElement>("JoinBoard", bookingId));
        strangerError.Message.ShouldContain(WhiteboardErrorCodes.NotParticipant);

        var send = await teacher.InvokeAsync<JsonElement>("SendElements", bookingId, new[] { Element("stroke-1", 1) });
        send.GetProperty("accepted").GetInt32().ShouldBe(1);
        send.GetProperty("corrections").GetArrayLength().ShouldBe(0);

        var elements = await received.Task.WaitAsync(Timeout);
        elements.Single().GetProperty("id").GetString().ShouldBe("stroke-1");
        teacherGotOwn.ShouldBeFalse(); // OthersInGroup: gönderen kendi çizimini geri almaz

        // Görsel reddi uçtan uca.
        var image = JsonDocument.Parse("{\"id\":\"img\",\"version\":1,\"type\":\"image\",\"fileId\":\"f\"}").RootElement;
        (await Should.ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>(
                () => teacher.InvokeAsync<JsonElement>("SendElements", bookingId, new[] { image })))
            .Message.ShouldContain(WhiteboardErrorCodes.ElementTypeNotAllowed);

        // Öğrenci düşüp yeniden bağlanır: sahne korunur.
        await using (var rejoin = Connect("kc-wb-student", "Student"))
        {
            await rejoin.StartAsync();
            var snapshot = await rejoin.InvokeAsync<JsonElement>("JoinBoard", bookingId);
            snapshot.GetProperty("elements").GetArrayLength().ShouldBe(1);
            snapshot.GetProperty("serverVersion").GetInt64().ShouldBe(1);
        }

        // Öğrencinin ikinci (yeniden) bağlantısı kapandı ama ilk bağlantısı hâlâ açık → çevrimdışı bildirilmez.
        studentOffline.Task.IsCompleted.ShouldBeFalse();
        await student.InvokeAsync("LeaveBoard", bookingId);
        (await studentOffline.Task.WaitAsync(Timeout)).ShouldBe(("student", false));
        await student.InvokeAsync<JsonElement>("JoinBoard", bookingId);

        // Randevu artık Approved değil → temizlik turu tahtayı kapatır, BoardClosed yayınlar, durum silinir.
        await WithDbAsync(async db =>
        {
            var booking = await db.Bookings.FindAsync(bookingId);
            booking!.Status = BookingStatus.Rejected;
            await db.SaveChangesAsync();
        });
        var cleanup = Factory.Services.GetServices<IHostedService>().OfType<WhiteboardCleanupService>().Single();
        (await cleanup.SweepAsync()).ShouldBeGreaterThanOrEqualTo(1);

        (await studentClosed.Task.WaitAsync(Timeout)).ShouldBe(WhiteboardCloseReasons.BookingCancelled);
        Factory.Services.GetRequiredService<IWhiteboardStore>().IsOpen(bookingId).ShouldBeFalse();

        (await Should.ThrowAsync<Microsoft.AspNetCore.SignalR.HubException>(
                () => teacher.InvokeAsync<JsonElement>("SendElements", bookingId, new[] { Element("stroke-2", 1) })))
            .Message.ShouldContain(WhiteboardErrorCodes.NotJoined);
    }

    [Fact]
    public async Task Teacher_connects_with_query_token_only_while_profile_cache_is_empty()
    {
        // Canlı E2E hatası: WebSocket'te token yalnızca query string ile gelir, Authorization header'ı yoktur. Profil
        // Redis'te yokken ApprovedTeacher kapısı auth-api'ye token'sız gidiyor → 401 → 500. Artık doğrulanmış istekte
        // saklanan token iletilir.
        const string sub = "kc-wb-teacher-cold";
        const string queryToken = "wb-teacher-query-token";
        var bookingId = await SeedBookingAsync();
        await SeedProfileAsync(StudentUserId, "kc-wb-student", "Student");
        var authApi = Factory.Services.GetRequiredService<FakeAuthApiProfiles>();
        authApi.Register(queryToken, new UserProfileDto
        {
            Id = TeacherUserId, KeycloakId = sub, Role = "Teacher", FullName = "Cold Teacher", Email = "c@t.local"
        });
        var cache = Factory.Services.GetRequiredService<IDistributedCache>();
        await cache.RemoveAsync(sub);

        var hubUrl = new Uri(Factory.Server.BaseAddress, "hub/whiteboard" + QueryString.Create("access_token", queryToken));
        await using var teacher = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                o.Headers["X-Test-Auth"] = sub;
                o.Headers["X-Test-Username"] = sub;
                o.Headers["X-Test-Roles"] = "Teacher";
            })
            .Build();

        await teacher.StartAsync();
        var join = await teacher.InvokeAsync<JsonElement>("JoinBoard", bookingId);

        join.GetProperty("role").GetString().ShouldBe("teacher");
        authApi.ServedTokens.ShouldContain(queryToken);
        (await cache.GetStringAsync(sub)).ShouldNotBeNull(); // provider profili cache'ledi
    }

    [Fact]
    public async Task Teacher_connects_over_websockets_with_query_token_and_reloads_profile_inside_the_hub()
    {
        // Üretim transport'u: WebSocket upgrade'inde header yok, yalnız query token. Negotiate'ten sonra cache yeniden
        // boşaltılır → JoinBoard (hub çağrısı, 30 sn'lik yeniden doğrulamayla aynı yol) profili WebSocket isteğinin
        // doğrulanmış token'ıyla yeniden yükler.
        const string sub = "kc-wb-teacher-ws";
        const string queryToken = "wb-teacher-ws-query-token";
        var bookingId = await SeedBookingAsync();
        await SeedProfileAsync(StudentUserId, "kc-wb-student", "Student");
        var authApi = Factory.Services.GetRequiredService<FakeAuthApiProfiles>();
        authApi.Register(queryToken, new UserProfileDto
        {
            Id = TeacherUserId, KeycloakId = sub, Role = "Teacher", FullName = "WS Teacher", Email = "w@t.local"
        });
        var cache = Factory.Services.GetRequiredService<IDistributedCache>();
        await cache.RemoveAsync(sub);

        void AddIdentity(IDictionary<string, string> headers)
        {
            headers["X-Test-Auth"] = sub;
            headers["X-Test-Username"] = sub;
            headers["X-Test-Roles"] = "Teacher";
        }

        var hubUrl = new Uri(Factory.Server.BaseAddress, "hub/whiteboard" + QueryString.Create("access_token", queryToken));
        await using var teacher = new HubConnectionBuilder()
            .WithUrl(hubUrl, o =>
            {
                o.Transports = HttpTransportType.WebSockets;
                o.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                AddIdentity(o.Headers);
                o.WebSocketFactory = async (context, ct) =>
                {
                    var wsClient = Factory.Server.CreateWebSocketClient();
                    wsClient.ConfigureRequest = request =>
                    {
                        request.Headers["X-Test-Auth"] = sub;
                        request.Headers["X-Test-Username"] = sub;
                        request.Headers["X-Test-Roles"] = "Teacher";
                    };
                    return await wsClient.ConnectAsync(context.Uri, ct);
                };
            })
            .Build();

        await teacher.StartAsync();
        var servedBeforeJoin = authApi.ServedTokens.Count(t => t == queryToken);
        await cache.RemoveAsync(sub);

        var join = await teacher.InvokeAsync<JsonElement>("JoinBoard", bookingId);

        join.GetProperty("role").GetString().ShouldBe("teacher");
        authApi.ServedTokens.Count(t => t == queryToken).ShouldBe(servedBeforeJoin + 1);
        (await cache.GetStringAsync(sub)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Anonymous_connection_is_rejected()
    {
        await using var anonymous = new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "hub/whiteboard"), o =>
            {
                o.Transports = HttpTransportType.LongPolling;
                o.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
            })
            .Build();

        var ex = await Should.ThrowAsync<HttpRequestException>(() => anonymous.StartAsync());
        ex.StatusCode.ShouldBe(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Hub_endpoint_closes_on_token_expiry()
    {
        // HttpConnectionDispatcherOptions, MapHub'ın negotiate endpoint'inin metadata'sında taşınır.
        var endpoint = Factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .First(e => e.RoutePattern.RawText == "/hub/whiteboard/negotiate");

        var options = endpoint.Metadata.GetMetadata<HttpConnectionDispatcherOptions>();
        options.ShouldNotBeNull();
        options.CloseOnAuthenticationExpiration.ShouldBeTrue();
        // Test factory'si LongPolling'i açar (Whiteboard__AllowedTransports); WebSockets her zaman dahil.
        options.Transports.ShouldBe(HttpTransportType.WebSockets | HttpTransportType.LongPolling);
    }
}
