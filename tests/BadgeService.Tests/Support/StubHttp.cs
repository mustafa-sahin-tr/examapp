using System.Net;

namespace BadgeService.Tests.Support;

/// <summary>
/// An <see cref="IHttpClientFactory"/> whose clients answer from a URL-substring routing table.
/// Every outgoing request is recorded on <see cref="Requests"/>.
/// </summary>
public sealed class StubHttp : IHttpClientFactory
{
    private readonly List<(string Match, Func<HttpRequestMessage, HttpResponseMessage> Handler)> _routes = new();

    public List<HttpRequestMessage> Requests { get; } = new();

    /// <summary>Request bodies, captured at send time (the caller often disposes its content afterwards).</summary>
    public Dictionary<HttpRequestMessage, string> Bodies { get; } = new();

    public string BodyMatching(string urlContains) =>
        Bodies.First(kv => kv.Key.RequestUri!.ToString().Contains(urlContains, StringComparison.OrdinalIgnoreCase)).Value;

    public StubHttp On(string urlContains, HttpStatusCode status, string body, string contentType = "application/json")
    {
        _routes.Add((urlContains, _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, contentType),
        }));
        return this;
    }

    public StubHttp OnBytes(string urlContains, byte[] body)
    {
        _routes.Add((urlContains, _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body),
        }));
        return this;
    }

    public HttpClient CreateClient(string name) => new(new Handler(this));

    private HttpResponseMessage Dispatch(HttpRequestMessage request)
    {
        Requests.Add(request);
        var url = request.RequestUri!.ToString();
        foreach (var (match, handler) in _routes)
            if (url.Contains(match, StringComparison.OrdinalIgnoreCase))
                return handler(request);
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"no stub route for {url}") };
    }

    private sealed class Handler(StubHttp owner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                owner.Bodies[request] = await request.Content.ReadAsStringAsync(cancellationToken);
            return owner.Dispatch(request);
        }
    }
}
