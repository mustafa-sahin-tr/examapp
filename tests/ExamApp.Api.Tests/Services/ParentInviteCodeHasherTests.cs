using ExamApp.Api.Services.Parents;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>issue #419: davet kodu üretimi (alfabe/uzunluk/rastgelelik), normalize ve HMAC hash + pepper kuralları.</summary>
public class ParentInviteCodeHasherTests
{
    internal const string TestPepper = "test-parent-invite-pepper-0123456789abcdef";

    internal static ParentInviteCodeHasher Create(string pepper = TestPepper, string environment = "Testing")
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environment);
        return new ParentInviteCodeHasher(Options.Create(new ParentLinkOptions { InviteCodePepper = pepper }), env);
    }

    [Fact]
    public void Generate_uses_unambiguous_alphabet_and_twelve_chars()
    {
        var hasher = Create();
        var codes = Enumerable.Range(0, 500).Select(_ => hasher.Generate()).ToList();

        codes.ShouldAllBe(c => c.Length == ParentLinkRules.CodeLength);
        codes.SelectMany(c => c).ShouldAllBe(ch => ParentLinkRules.Alphabet.Contains(ch));
        string.Concat(codes).IndexOfAny(['0', 'O', '1', 'I', 'L']).ShouldBe(-1);
        codes.Distinct().Count().ShouldBe(codes.Count); // 31^12 uzayında 500 kodda çakışma beklenmez
    }

    [Theory]
    [InlineData("abcd-efgh-jkmn", "ABCDEFGHJKMN")]
    [InlineData(" ABCD EFGH JKMN ", "ABCDEFGHJKMN")]
    [InlineData("k7m2p9qzr4st", "K7M2P9QZR4ST")]
    public void Normalize_accepts_case_spaces_and_dash(string input, string expected)
        => Create().Normalize(input).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDEFGH")]         // eski 8 karakterlik biçim
    [InlineData("ABCDEFGHJKM")]      // kısa (11)
    [InlineData("ABCDEFGHJKMNP")]    // uzun (13)
    [InlineData("ABCDEFGHJKM0")]     // alfabe dışı (0)
    [InlineData("ABCDEFGHJKMI")]     // alfabe dışı (I)
    [InlineData("ABCD_EFGH_JKM")]
    [InlineData("ÇBCDEFGHJKMN")]
    public void Normalize_rejects_malformed_input(string? input) => Create().Normalize(input).ShouldBeNull();

    [Fact]
    public void Hash_is_deterministic_hex_and_never_contains_the_code()
    {
        var hasher = Create();
        var hash = hasher.Hash("ABCDEFGHJKMN");

        hash.Length.ShouldBe(64);
        hash.ShouldMatch("^[0-9a-f]{64}$");
        hasher.Hash("ABCDEFGHJKMN").ShouldBe(hash);
        hash.ShouldNotContain("ABCDEFGHJKMN", Case.Insensitive);
        hasher.Hash("ABCDEFGHJKMP").ShouldNotBe(hash);
    }

    [Fact]
    public void Hash_depends_on_the_server_pepper()
        => Create(TestPepper).Hash("ABCDEFGHJKMN").ShouldNotBe(Create(TestPepper + "x").Hash("ABCDEFGHJKMN"));

    [Fact]
    public void Development_without_pepper_falls_back_to_dev_only_value()
        => Create(pepper: "", environment: "Development").Hash("ABCDEFGHJKMN").Length.ShouldBe(64);

    [Fact]
    public void Options_validator_fails_startup_outside_development_and_passes_in_development()
    {
        var prod = Substitute.For<IHostEnvironment>();
        prod.EnvironmentName.Returns("Production");
        var dev = Substitute.For<IHostEnvironment>();
        dev.EnvironmentName.Returns("Development");

        new ParentLinkOptionsValidator(prod).Validate(null, new ParentLinkOptions()).Failed.ShouldBeTrue();
        new ParentLinkOptionsValidator(prod).Validate(null, new ParentLinkOptions { InviteCodePepper = TestPepper }).Succeeded.ShouldBeTrue();
        new ParentLinkOptionsValidator(dev).Validate(null, new ParentLinkOptions()).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    [InlineData("devOnlyParentInvitePepperChangeMe0123456789")]
    public void Non_development_rejects_missing_short_or_dev_only_pepper(string pepper)
        => Should.Throw<InvalidOperationException>(() => Create(pepper, "Production").Hash("ABCDEFGHJKMN"));
}
