using System.Net;
using System.Net.Http.Json;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #84: <c>POST /api/login-events</c> is service-to-service only (BadgeService's
/// LoginAttemptedConsumer is the only caller) — never reachable by an end-user token or
/// anonymously.
/// </summary>
public class LoginEventsEndpointTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private static LoginEventCreateDto ValidEvent() => new()
    {
        KeycloakUserId = "kc-login-events-1",
        Role = "Student",
        OccurredAtUtc = DateTime.UtcNow,
        Success = true,
    };

    [Fact]
    public async Task Create_rejects_anonymous_callers()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/login-events", ValidEvent());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_is_forbidden_for_an_authenticated_end_user_without_the_service_role()
    {
        var student = await ClientAsAsync(1, "Student", "kc-login-events-caller", "Student");

        var response = await student.PostAsJsonAsync("/api/login-events", ValidEvent());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Create_passes_the_authorization_gate_for_a_service_to_service_caller()
    {
        var service = ServiceClient();

        var response = await service.PostAsJsonAsync("/api/login-events", ValidEvent());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
