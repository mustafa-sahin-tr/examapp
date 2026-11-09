using BadgeService.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BadgeService.Tests;

public class SmtpEmailSenderTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => $"Email:Smtp:{v.Key}", v => (string?)v.Value))
            .Build();

    private static Type? SenderType(IConfiguration configuration) =>
        new ServiceCollection().AddEmailSender(configuration)
            .Single(d => d.ServiceType == typeof(IEmailSender))
            .ImplementationType;

    [Fact]
    public void NoHost_KeepsTheLoggingSender()
    {
        Assert.Equal(typeof(LoggingEmailSender), SenderType(Config()));
    }

    [Fact]
    public void Host_UsesSmtp()
    {
        var config = Config(("Host", "smtp.test"), ("Username", "u"), ("Password", "p"), ("FromAddress", "from@example.test"));

        Assert.Equal(typeof(SmtpEmailSender), SenderType(config));
    }

    [Theory]
    [InlineData("Username")]
    [InlineData("Password")]
    [InlineData("FromAddress")]
    public void Host_WithMissingSetting_FailsAtStartup(string missing)
    {
        var values = new[] { ("Host", "smtp.test"), ("Username", "u"), ("Password", "p"), ("FromAddress", "from@example.test") }
            .Where(v => v.Item1 != missing)
            .ToArray();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddEmailSender(Config(values)));
    }

    [Fact]
    public void BuildMessage_SetsAddressesSubjectAndBothBodies()
    {
        var options = new SmtpEmailOptions { FromAddress = "from@example.test", FromName = "Hedef Okul" };

        var mime = SmtpEmailSender.BuildMessage(new EmailMessage("parent@example.test", "Davet", "metin", "<p>html</p>"), options);

        Assert.Equal("Davet", mime.Subject);
        Assert.Equal("\"Hedef Okul\" <from@example.test>", mime.From.ToString());
        Assert.Equal("parent@example.test", mime.To.ToString());
        Assert.Equal("metin", mime.TextBody.Trim());
        Assert.Equal("<p>html</p>", mime.HtmlBody.Trim());
    }
}
