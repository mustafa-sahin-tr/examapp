using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Support;

/// <summary>review #576: tohumlu (deterministik) <see cref="IInteractionRandom"/> — kayıtta karıştırma testleri için.</summary>
public sealed class SeededInteractionRandom(int seed) : IInteractionRandom
{
    private readonly Random _random = new(seed);

    public int Next(int maxExclusive) => _random.Next(maxExclusive);
}
