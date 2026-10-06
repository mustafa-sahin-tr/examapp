using ExamApp.Foundation.Messaging;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Foundation.Tests;

public class RabbitMqConnectionSettingsTests
{
    private const string SecretPassword = "s3cr3t-pw-DO-NOT-LEAK";

    private static IConfiguration Config(string? host, string? user, string? password)
    {
        var d = new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = host,
            ["RabbitMQ:Username"] = user,
            ["RabbitMQ:Password"] = password,
        };
        return new ConfigurationBuilder().AddInMemoryCollection(d).Build();
    }

    [Fact]
    public void Require_returns_settings_when_all_present()
    {
        var s = RabbitMqConnectionSettings.Require(Config("rabbitmq", "badge_service", SecretPassword));
        s.Host.ShouldBe("rabbitmq");
        s.Username.ShouldBe("badge_service");
        s.Password.ShouldBe(SecretPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Require_throws_when_username_missing(string? user)
    {
        var ex = Should.Throw<InvalidOperationException>(
            () => RabbitMqConnectionSettings.Require(Config("rabbitmq", user, SecretPassword)));
        ex.Message.ShouldContain("RabbitMQ:Username");
        ex.Message.ShouldNotContain(SecretPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Require_throws_when_password_missing(string? password)
    {
        var ex = Should.Throw<InvalidOperationException>(
            () => RabbitMqConnectionSettings.Require(Config("rabbitmq", "badge_service", password)));
        ex.Message.ShouldContain("RabbitMQ:Password");
        ex.Message.ShouldNotContain("badge_service");
    }

    [Fact]
    public void Require_throws_when_host_missing()
    {
        var ex = Should.Throw<InvalidOperationException>(
            () => RabbitMqConnectionSettings.Require(Config(null, "badge_service", SecretPassword)));
        ex.Message.ShouldContain("RabbitMQ:Host");
        ex.Message.ShouldNotContain(SecretPassword);
    }

    [Fact]
    public void Require_never_falls_back_to_guest()
    {
        var ex = Should.Throw<InvalidOperationException>(() => RabbitMqConnectionSettings.Require(Config("rabbitmq", null, null)));
        ex.Message.ShouldContain("guest hesabı kullanılmaz");
    }

    [Fact]
    public void ToString_masks_the_password()
    {
        var s = RabbitMqConnectionSettings.Require(Config("rabbitmq", "badge_service", SecretPassword));
        var text = s.ToString();
        text.ShouldNotContain(SecretPassword);
        text.ShouldContain("***");
        text.ShouldContain("badge_service");
    }
}
