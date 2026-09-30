using ExamApp.Api.Hubs;
using ExamApp.Api.Services.Whiteboard;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services.Whiteboard;

/// <summary>issue #98 — DI kaydı: 128 KB mesaj sınırı yalnızca whiteboard hub'ında; varsayılan transport yalnızca WebSockets.</summary>
public class WhiteboardServiceRegistrationTests
{
    private sealed class OtherHub : Hub;

    private static ServiceProvider Build(Dictionary<string, string?>? config = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(config ?? new()).Build());
        services.AddLogging();
        services.AddWhiteboard();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Max_receive_message_size_is_128KB_for_whiteboard_hub_only()
    {
        using var sp = Build();

        var whiteboard = sp.GetRequiredService<IOptions<HubOptions<WhiteboardHub>>>().Value;
        var global = sp.GetRequiredService<IOptions<HubOptions>>().Value;
        var other = sp.GetRequiredService<IOptions<HubOptions<OtherHub>>>().Value;

        whiteboard.MaximumReceiveMessageSize.ShouldBe(128 * 1024);
        whiteboard.EnableDetailedErrors.ShouldBe(false);
        // HubConnectionHandler hub'a özel değerleri YALNIZCA dahili UserHasSetValues=true ise kullanır (AddHubOptions ile
        // gelen HubOptionsSetup set eder); aksi halde sessizce global sınıra düşer.
        UserHasSetValues(whiteboard).ShouldBeTrue();

        global.MaximumReceiveMessageSize.ShouldBe(32 * 1024);
        UserHasSetValues(other).ShouldBeFalse(); // başka hub → global 32 KB geçerli
        (other.MaximumReceiveMessageSize ?? global.MaximumReceiveMessageSize).ShouldBe(32 * 1024);
    }

    /// <summary>HubOptions&lt;THub&gt;'ın dahili bayrağı (HubOptionsSetup&lt;THub&gt; set eder).</summary>
    private static bool UserHasSetValues(HubOptions options)
        => (bool)options.GetType().GetProperty("UserHasSetValues",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(options)!;

    [Fact]
    public void Default_transport_is_websockets_only_and_config_can_widen_it()
    {
        using (var sp = Build())
            sp.GetRequiredService<IOptions<WhiteboardOptions>>().Value.AllowedTransports.ShouldBe(HttpTransportType.WebSockets);

        using (var sp = Build(new() { ["Whiteboard:AllowedTransports"] = "WebSockets, LongPolling" }))
            sp.GetRequiredService<IOptions<WhiteboardOptions>>().Value.AllowedTransports
                .ShouldBe(HttpTransportType.WebSockets | HttpTransportType.LongPolling);
    }
}
