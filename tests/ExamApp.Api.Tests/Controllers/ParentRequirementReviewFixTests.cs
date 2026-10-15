using System.Net;
using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Parents.Enforcement;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #440/#441 review fix round: süre dolan öğrenci eski (#419) isteği onaylayıp reddedebilir (allowlist, code MINOR2); aktivasyon rate
/// limit anahtarı ilk X-Forwarded-For'a koşulsuz güvenmez (security MINOR2); durum çözücüsü yalnız profil sağlayıcı hatalarında fail-open,
/// DB hatalarını yutmaz (code MINOR1).
/// </summary>
public class ParentRequirementReviewFixTests
{
    [Theory]
    [InlineData(nameof(ParentLinksController.Approve))]
    [InlineData(nameof(ParentLinksController.Reject))]
    public void Student_legacy_approve_and_reject_stay_open_after_grace_expiry(string action)
        => typeof(ParentLinksController).GetMethod(action)!.GetCustomAttribute<AllowWhenParentRequiredAttribute>().ShouldNotBeNull();

    private static HttpContext Context(string remote, string? forwarded)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        if (forwarded != null)
            http.Request.Headers["X-Forwarded-For"] = forwarded;
        return http;
    }

    [Fact]
    public void Forwarded_header_is_trusted_only_from_a_trusted_proxy()
    {
        // Varsayılan (yapılandırma yok): YALNIZ loopback (Aspire gateway'i); SON değer (en yakın proxy'nin eklediği).
        TrustedClientIp.Resolve(Context("127.0.0.1", "203.0.113.7, 198.51.100.2"), null).ShouldBe("198.51.100.2");
        // Özel ağdaki herhangi bir konteyner / genel IP başlığı sahteleyemez.
        TrustedClientIp.Resolve(Context("172.18.0.5", "1.2.3.4"), null).ShouldBe("172.18.0.5");
        TrustedClientIp.Resolve(Context("198.51.100.99", "1.2.3.4"), null).ShouldBe("198.51.100.99");
        TrustedClientIp.Resolve(Context("127.0.0.1", "not-an-ip"), null).ShouldBe("127.0.0.1");

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.9"
        }).Build();
        TrustedClientIp.Resolve(Context("10.0.0.9", "203.0.113.7"), config).ShouldBe("203.0.113.7");
        TrustedClientIp.Resolve(Context("10.0.0.10", "203.0.113.7"), config).ShouldBe("10.0.0.10"); // listede değil
        var compose = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12" // docker-compose ayarı
        }).Build();
        TrustedClientIp.Resolve(Context("172.18.0.5", "203.0.113.8"), compose).ShouldBe("203.0.113.8");
        TrustedClientIp.Resolve(Context("127.0.0.1", "2001:db8:1:2:3:4:5:6"), null).ShouldBe("2001:db8:1:2::/64");
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
        => new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void Outside_development_the_trusted_proxy_configuration_is_mandatory_and_must_parse()
    {
        // Security re-review r3 M1: boş ya da yer tutuculu yapılandırma açılışı durdurur.
        Should.Throw<InvalidOperationException>(() => TrustedClientIp.ValidateTrustedProxyConfiguration(Config()));
        Should.Throw<InvalidOperationException>(() => TrustedClientIp.ValidateTrustedProxyConfiguration(
            Config(("ForwardedHeaders:KnownNetworks:0", "REPLACE_POD_CIDR"))));
        Should.Throw<InvalidOperationException>(() => TrustedClientIp.ValidateTrustedProxyConfiguration(
            Config(("ForwardedHeaders:KnownProxies:0", "gateway"))));
        Should.Throw<InvalidOperationException>(() => TrustedClientIp.ValidateTrustedProxyConfiguration(
            Config(("ForwardedHeaders:KnownNetworks:0", "0.0.0.0/0"))));

        TrustedClientIp.ValidateTrustedProxyConfiguration(Config(("ForwardedHeaders:KnownProxies:0", "172.30.0.10")));
        TrustedClientIp.ValidateTrustedProxyConfiguration(Config(("ForwardedHeaders:KnownNetworks:0", "10.8.0.0/14")));
    }

    private static ClaimsPrincipal StudentPrincipal() => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, "kc-1"), new Claim(ClaimTypes.Role, "Student")
    }, "TestAuth"));

    private static StudentParentStatusResolver Resolver(IUserProfileProvider profiles, IStudentParentStatusService status)
        => new(profiles, status, new ConfigurationBuilder().Build(), NullLogger<StudentParentStatusResolver>.Instance);

    [Fact]
    public async Task Profile_provider_outage_fails_open()
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync("kc-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UserProfileDto>(new HttpRequestException("auth-api down")));

        (await Resolver(profiles, Substitute.For<IStudentParentStatusService>()).ResolveAsync(StudentPrincipal())).ShouldBeNull();
    }

    [Fact]
    public async Task Database_errors_are_not_swallowed()
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync("kc-1", Arg.Any<CancellationToken>()).Returns(new UserProfileDto { Id = 1, KeycloakId = "kc-1" });
        var status = Substitute.For<IStudentParentStatusService>();
        status.GetAsync(1, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StudentParentState>(new InvalidOperationException("db down")));

        await Should.ThrowAsync<InvalidOperationException>(() => Resolver(profiles, status).ResolveAsync(StudentPrincipal()));
    }
}
