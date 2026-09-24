namespace ExamApp.Api.Tests.Support;

/// <summary>Sabit "şimdi" döndüren <see cref="TimeProvider"/> (issue #265: yerel gün sınırı testleri).</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
