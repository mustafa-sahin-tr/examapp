using System.Security.Cryptography;
using ExamApp.Foundation.Security;

namespace ExamApp.Foundation.Tests.Security;

public class ActivationLinkProtectorTests
{
    private static ActivationLinkProtector New(byte fill = 7) => new(Enumerable.Repeat(fill, 32).Select(b => b).ToArray());

    [Fact]
    public void Roundtrip_returns_original_and_is_not_plaintext()
    {
        var p = New();
        var url = "https://x.test/app/parent-activate#token=abc123";
        var protectedValue = p.Protect(url);
        protectedValue.ShouldStartWith("v1.");
        protectedValue.ShouldNotContain("abc123");
        p.Unprotect(protectedValue).ShouldBe(url);
    }

    [Fact]
    public void Protect_uses_fresh_nonce()
        => New().Protect("same").ShouldNotBe(New().Protect("same"));

    [Fact]
    public void Tampered_payload_throws()
    {
        var p = New();
        var v = p.Protect("secret");
        var parts = v.Split('.');
        var ct = parts[2];
        parts[2] = (ct[0] == 'A' ? 'B' : 'A') + ct[1..];
        Should.Throw<CryptographicException>(() => p.Unprotect(string.Join('.', parts)));
    }

    [Fact]
    public void Wrong_key_throws()
        => Should.Throw<CryptographicException>(() => New(8).Unprotect(New(7).Protect("secret")));

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("v2.a.b")]
    [InlineData("v1.!!.!!")]
    public void Malformed_input_throws_cryptographic_exception(string input)
        => Should.Throw<CryptographicException>(() => New().Unprotect(input));

    [Fact]
    public void ResolveKey_development_falls_back_to_dev_key_when_empty()
        => ActivationLinkProtector.ResolveKey("", isDevelopment: true).Length.ShouldBe(32);

    [Fact]
    public void ResolveKey_non_development_rejects_empty_and_devOnly_keys()
    {
        Should.Throw<InvalidOperationException>(() => ActivationLinkProtector.ResolveKey("", isDevelopment: false));
        Should.Throw<InvalidOperationException>(() => ActivationLinkProtector.ResolveKey(ActivationLinkProtector.DevKeyBase64, isDevelopment: false));
        ActivationLinkProtector.ResolveKey(ActivationLinkProtector.DevKeyBase64, isDevelopment: true).Length.ShouldBe(32);
    }

    [Fact]
    public void ResolveKey_rejects_wrong_length_and_non_base64()
    {
        Should.Throw<InvalidOperationException>(() => ActivationLinkProtector.ResolveKey(Convert.ToBase64String(new byte[16]), true));
        Should.Throw<InvalidOperationException>(() => ActivationLinkProtector.ResolveKey("not base64!!", true));
    }

    [Fact]
    public void ResolveKey_accepts_real_32_byte_key_in_production()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        ActivationLinkProtector.ResolveKey(Convert.ToBase64String(key), isDevelopment: false).ShouldBe(key);
    }
}
