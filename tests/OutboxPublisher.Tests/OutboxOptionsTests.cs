using OutboxPublisherService.Publishers;

namespace OutboxPublisher.Tests;

public class OutboxOptionsTests
{
    private readonly OutboxOptions _options = new()
    {
        RetryBackoffBase = TimeSpan.FromSeconds(10),
        RetryBackoffMax = TimeSpan.FromMinutes(30),
    };

    [Theory]
    [InlineData(1, 10)]    // base * 2^0
    [InlineData(2, 20)]    // base * 2^1
    [InlineData(3, 40)]
    [InlineData(4, 80)]
    [InlineData(5, 160)]
    public void ComputeBackoff_grows_exponentially_from_the_base(int attempt, int expectedSeconds)
        => _options.ComputeBackoff(attempt).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));

    [Fact]
    public void ComputeBackoff_is_capped_at_the_max()
    {
        _options.ComputeBackoff(10).ShouldBe(_options.RetryBackoffMax);
        _options.ComputeBackoff(50).ShouldBe(_options.RetryBackoffMax);
    }

    [Fact]
    public void ComputeBackoff_does_not_overflow_on_a_huge_attempt()
        => Should.NotThrow(() => _options.ComputeBackoff(int.MaxValue))
            .ShouldBe(_options.RetryBackoffMax);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ComputeBackoff_treats_a_non_positive_attempt_as_the_first(int attempt)
        => _options.ComputeBackoff(attempt).ShouldBe(_options.RetryBackoffBase);

    [Fact]
    public void Defaults_are_sane()
    {
        var d = new OutboxOptions();
        d.BatchSize.ShouldBeGreaterThan(0);
        d.MaxRetries.ShouldBeGreaterThan(0);
        d.PollInterval.ShouldBeGreaterThan(TimeSpan.Zero);
        d.Retention.ShouldBeGreaterThan(TimeSpan.Zero);
    }
}
