using Microsoft.AspNetCore.Http;
using Shouldly;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// security review O1: query token'ı yalnızca hub yolunda ve Authorization header'ı YOKKEN kabul edilir. Header varken
/// JwtBearer header'ı doğrular — kimliği belirleyen token ile aşağı akışa iletilen token aynı kalır.
/// </summary>
public class SignalRQueryTokenTests
{
    private const string HubPath = "/hub/whiteboard";

    private static HttpRequest Request(string path, string? queryToken, string? authorizationHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (queryToken is not null)
            context.Request.QueryString = QueryString.Create(SignalRQueryToken.QueryParameter, queryToken);
        if (authorizationHeader is not null)
            context.Request.Headers.Authorization = authorizationHeader;
        return context.Request;
    }

    [Fact]
    public void Query_token_is_accepted_on_the_hub_path_without_a_header() =>
        SignalRQueryToken.Resolve(Request(HubPath + "/negotiate", "token-a-example"), HubPath).ShouldBe("token-a-example");

    [Fact]
    public void Query_token_is_ignored_when_an_authorization_header_is_present() =>
        SignalRQueryToken.Resolve(Request(HubPath, "token-a-example", "Bearer token-b-example"), HubPath).ShouldBeNull();

    [Fact]
    public void Query_token_is_ignored_outside_the_hub_path() =>
        SignalRQueryToken.Resolve(Request("/api/something", "token-a-example"), HubPath).ShouldBeNull();

    [Fact]
    public void Missing_query_token_resolves_to_null() =>
        SignalRQueryToken.Resolve(Request(HubPath, null), HubPath).ShouldBeNull();
}
