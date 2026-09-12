using ExamApp.Api.Services.Video;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace ExamApp.Api.Tests.Services.Video;

/// <summary>
/// Issue #97 — DI registration for video sessions: Jitsi provider selection and validation.
/// </summary>
public class VideoServiceCollectionExtensionsTests
{
    [Fact]
    public void AddVideoSessions_WithJitsiProvider_RegistersJitsiVideoSessionProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:Provider", "Jitsi" },
                { "Video:JoinWindowBeforeMinutes", "15" },
                { "Video:JoinWindowAfterMinutes", "30" },
                { "Video:Jitsi:PublicBaseUrl", "http://localhost:8000" },
                { "Video:Jitsi:AppId", "examapp" },
                { "Video:Jitsi:XmppDomain", "meet.jitsi" },
                { "Video:Jitsi:TokenLifetimeMinutes", "180" }
            })
            .Build();

        var services = new ServiceCollection();
        var hostEnv = Substitute.For<IHostEnvironment>();
        hostEnv.EnvironmentName.Returns(Environments.Development);
        services.AddSingleton(hostEnv);

        services.AddVideoSessions(config);
        var provider = services.BuildServiceProvider();

        var videoSessionProvider = provider.GetService<IVideoSessionProvider>();
        Assert.NotNull(videoSessionProvider);
        Assert.IsType<JitsiVideoSessionProvider>(videoSessionProvider);
    }

    [Fact]
    public void AddVideoSessions_WithoutProvider_DefaultsToJitsi()
    {
        // When Video:Provider is not set, it should default to Jitsi
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:JoinWindowBeforeMinutes", "15" },
                { "Video:JoinWindowAfterMinutes", "30" },
                { "Video:Jitsi:PublicBaseUrl", "http://localhost:8000" },
                { "Video:Jitsi:AppId", "examapp" },
                { "Video:Jitsi:XmppDomain", "meet.jitsi" },
                { "Video:Jitsi:TokenLifetimeMinutes", "180" }
            })
            .Build();

        var services = new ServiceCollection();
        var hostEnv = Substitute.For<IHostEnvironment>();
        hostEnv.EnvironmentName.Returns(Environments.Development);
        services.AddSingleton(hostEnv);

        services.AddVideoSessions(config);
        var provider = services.BuildServiceProvider();

        var videoSessionProvider = provider.GetService<IVideoSessionProvider>();
        Assert.NotNull(videoSessionProvider);
        Assert.IsType<JitsiVideoSessionProvider>(videoSessionProvider);
    }

    [Fact]
    public void AddVideoSessions_WithUnknownProvider_ThrowsInvalidOperation()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:Provider", "UnknownProvider" },
                { "Video:JoinWindowBeforeMinutes", "15" },
                { "Video:JoinWindowAfterMinutes", "30" },
                { "Video:Jitsi:PublicBaseUrl", "http://localhost:8000" },
                { "Video:Jitsi:AppId", "examapp" },
                { "Video:Jitsi:XmppDomain", "meet.jitsi" },
                { "Video:Jitsi:TokenLifetimeMinutes", "180" }
            })
            .Build();

        var services = new ServiceCollection();
        var ex = Assert.Throws<InvalidOperationException>(() => services.AddVideoSessions(config));

        Assert.Contains("Desteklenmeyen", ex.Message);
        Assert.Contains("UnknownProvider", ex.Message);
    }

    [Fact]
    public void AddVideoSessions_WithCaseInsensitiveJitsi_Succeeds()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:Provider", "JITSI" },
                { "Video:JoinWindowBeforeMinutes", "15" },
                { "Video:JoinWindowAfterMinutes", "30" },
                { "Video:Jitsi:PublicBaseUrl", "http://localhost:8000" },
                { "Video:Jitsi:AppId", "examapp" },
                { "Video:Jitsi:XmppDomain", "meet.jitsi" },
                { "Video:Jitsi:TokenLifetimeMinutes", "180" }
            })
            .Build();

        var services = new ServiceCollection();
        var hostEnv = Substitute.For<IHostEnvironment>();
        hostEnv.EnvironmentName.Returns(Environments.Development);
        services.AddSingleton(hostEnv);

        services.AddVideoSessions(config);
        var provider = services.BuildServiceProvider();

        var videoSessionProvider = provider.GetService<IVideoSessionProvider>();
        Assert.NotNull(videoSessionProvider);
        Assert.IsType<JitsiVideoSessionProvider>(videoSessionProvider);
    }

    [Fact]
    public void AddVideoSessions_RegistersTimeProvider()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:Provider", "Jitsi" },
                { "Video:JoinWindowBeforeMinutes", "15" },
                { "Video:JoinWindowAfterMinutes", "30" },
                { "Video:Jitsi:PublicBaseUrl", "http://localhost:8000" },
                { "Video:Jitsi:AppId", "examapp" },
                { "Video:Jitsi:XmppDomain", "meet.jitsi" },
                { "Video:Jitsi:TokenLifetimeMinutes", "180" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddVideoSessions(config);
        var provider = services.BuildServiceProvider();

        var timeProvider = provider.GetService<TimeProvider>();
        Assert.NotNull(timeProvider);
    }

    [Fact]
    public void AddVideoSessions_RegistersVideoOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:Provider", "Jitsi" },
                { "Video:JoinWindowBeforeMinutes", "15" },
                { "Video:JoinWindowAfterMinutes", "30" },
                { "Video:Jitsi:PublicBaseUrl", "http://localhost:8000" },
                { "Video:Jitsi:AppId", "examapp" },
                { "Video:Jitsi:XmppDomain", "meet.jitsi" },
                { "Video:Jitsi:TokenLifetimeMinutes", "180" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddVideoSessions(config);
        var provider = services.BuildServiceProvider();

        var options = provider.GetService<Microsoft.Extensions.Options.IOptions<VideoOptions>>();
        Assert.NotNull(options);
        Assert.NotNull(options.Value);
        Assert.Equal(15, options.Value.JoinWindowBeforeMinutes);
        Assert.Equal(30, options.Value.JoinWindowAfterMinutes);
    }

    [Fact]
    public void AddVideoSessions_BindsJitsiOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:Provider", "Jitsi" },
                { "Video:Jitsi:PublicBaseUrl", "http://custom.jitsi:8080" },
                { "Video:Jitsi:AppId", "customapp" },
                { "Video:Jitsi:XmppDomain", "custom.meet" },
                { "Video:Jitsi:TokenLifetimeMinutes", "120" },
                { "Video:JoinWindowBeforeMinutes", "20" },
                { "Video:JoinWindowAfterMinutes", "40" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddVideoSessions(config);
        var provider = services.BuildServiceProvider();

        var options = provider.GetService<Microsoft.Extensions.Options.IOptions<VideoOptions>>();
        Assert.NotNull(options);
        Assert.Equal("http://custom.jitsi:8080", options.Value.Jitsi.PublicBaseUrl);
        Assert.Equal("customapp", options.Value.Jitsi.AppId);
        Assert.Equal("custom.meet", options.Value.Jitsi.XmppDomain);
        Assert.Equal(120, options.Value.Jitsi.TokenLifetimeMinutes);
        Assert.Equal(20, options.Value.JoinWindowBeforeMinutes);
        Assert.Equal(40, options.Value.JoinWindowAfterMinutes);
    }

    [Fact]
    public void AddVideoSessions_InvalidJoinWindowBeforeMinutes_ThrowsValidationException()
    {
        // JoinWindowBeforeMinutes must be in range [0, 1440]
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:Provider", "Jitsi" },
                { "Video:JoinWindowBeforeMinutes", "-1" }, // Out of range
                { "Video:JoinWindowAfterMinutes", "30" },
                { "Video:Jitsi:PublicBaseUrl", "http://localhost:8000" },
                { "Video:Jitsi:AppId", "examapp" },
                { "Video:Jitsi:XmppDomain", "meet.jitsi" },
                { "Video:Jitsi:TokenLifetimeMinutes", "180" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddVideoSessions(config);

        var ex = Assert.Throws<OptionsValidationException>(() =>
            services.BuildServiceProvider().GetRequiredService<IStartupValidator>().Validate());

        Assert.NotNull(ex);
    }

    [Fact]
    public void AddVideoSessions_EmptyPublicBaseUrl_ThrowsValidationException()
    {
        // PublicBaseUrl must be a valid absolute URL
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:Provider", "Jitsi" },
                { "Video:JoinWindowBeforeMinutes", "15" },
                { "Video:JoinWindowAfterMinutes", "30" },
                { "Video:Jitsi:PublicBaseUrl", "" }, // Empty URL
                { "Video:Jitsi:AppId", "examapp" },
                { "Video:Jitsi:XmppDomain", "meet.jitsi" },
                { "Video:Jitsi:TokenLifetimeMinutes", "180" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddVideoSessions(config);

        var ex = Assert.Throws<OptionsValidationException>(() =>
            services.BuildServiceProvider().GetRequiredService<IStartupValidator>().Validate());

        Assert.NotNull(ex);
    }

    [Fact]
    public void AddVideoSessions_InvalidUrlFormat_ThrowsValidationException()
    {
        // PublicBaseUrl must be a valid absolute URI
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Video:Provider", "Jitsi" },
                { "Video:JoinWindowBeforeMinutes", "15" },
                { "Video:JoinWindowAfterMinutes", "30" },
                { "Video:Jitsi:PublicBaseUrl", "not-a-valid-url" }, // Relative URL
                { "Video:Jitsi:AppId", "examapp" },
                { "Video:Jitsi:XmppDomain", "meet.jitsi" },
                { "Video:Jitsi:TokenLifetimeMinutes", "180" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddVideoSessions(config);

        var ex = Assert.Throws<OptionsValidationException>(() =>
            services.BuildServiceProvider().GetRequiredService<IStartupValidator>().Validate());

        Assert.NotNull(ex);
    }
}
