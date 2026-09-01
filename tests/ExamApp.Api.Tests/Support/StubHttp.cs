using System.Net;

namespace ExamApp.Api.Tests.Support;

/// <summary>
/// An <see cref="IHttpClientFactory"/> whose clients are driven by a single
/// handler function. Records every request for assertions.
/// </summary>
public sealed class StubHttp : IHttpClientFactory
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public List<HttpRequestMessage> Requests { get; } = new();

    public StubHttp(HttpStatusCode status, string body)
        : this(_ => new HttpResponseMessage(status) { Content = new StringContent(body) }) { }

    public StubHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public HttpClient CreateClient(string name) => new(new Handler(this));

    private sealed class Handler(StubHttp owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            owner.Requests.Add(request);
            return Task.FromResult(owner._respond(request));
        }
    }
}
