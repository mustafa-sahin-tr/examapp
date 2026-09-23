using ExamApp.Api.Helpers;
using Microsoft.Extensions.Configuration;

namespace AuthApi.Tests.Hosting;

/// <summary>
/// Issue #100: auth-api's Kestrel bind address is selected by <c>Kestrel:BindLoopbackOnly</c>.
/// Aspire (host process) sets it to true so auth-api is unreachable from the LAN; the default stays
/// "all interfaces" because in a container the gateway connects from another network namespace.
/// </summary>
public class KestrelBindingTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();

    [Fact]
    public void Defaults_to_all_interfaces_and_port_5079()
    {
        var config = Config();

        KestrelBinding.IsLoopbackOnly(config).ShouldBeFalse();
        KestrelBinding.ResolvePort(config).ShouldBe(5079);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    public void Bind_loopback_only_is_read_from_configuration(string raw, bool expected)
    {
        var config = Config((KestrelBinding.BindLoopbackOnlyKey, raw));

        KestrelBinding.IsLoopbackOnly(config).ShouldBe(expected);
    }

    [Fact]
    public void Aspire_style_environment_keys_select_loopback_on_the_pinned_port()
    {
        // AppHost sets Kestrel__Port=6079 and Kestrel__BindLoopbackOnly=true (env var → "Kestrel:..." key).
        var config = Config(("Kestrel:Port", "6079"), ("Kestrel:BindLoopbackOnly", "true"));

        KestrelBinding.ResolvePort(config).ShouldBe(6079);
        KestrelBinding.IsLoopbackOnly(config).ShouldBeTrue();
    }
}
