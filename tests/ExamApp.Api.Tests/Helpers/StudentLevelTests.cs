using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>issue #243: Level = 1 + floor(sqrt(XP / 50)), XP &lt; 50 (negatif dahil) → 1.</summary>
public class StudentLevelTests
{
    [Theory]
    [InlineData(0L, 1)]
    [InlineData(1L, 1)]
    [InlineData(49L, 1)]
    [InlineData(50L, 2)]
    [InlineData(51L, 2)]
    [InlineData(199L, 2)]
    [InlineData(200L, 3)]
    [InlineData(449L, 3)]
    [InlineData(450L, 4)]
    [InlineData(799L, 4)]
    [InlineData(800L, 5)]
    [InlineData(4999L, 10)]
    [InlineData(5000L, 11)]
    public void FromXp_follows_the_formula_table_and_boundaries(long xp, int expected)
    {
        StudentLevel.FromXp(xp).ShouldBe(expected);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(-50L)]
    [InlineData(long.MinValue)]
    public void Negative_xp_is_level_one(long xp)
    {
        StudentLevel.FromXp(xp).ShouldBe(1);
    }

    [Fact]
    public void Int_max_xp_is_exact()
    {
        // int.MaxValue / 50 = 42_949_672 → isqrt = 6553 (6553² = 42_941_809, 6554² = 42_954_916)
        StudentLevel.FromXp(int.MaxValue).ShouldBe(6554);
    }

    [Fact]
    public void Long_max_xp_does_not_overflow_and_is_exact()
    {
        var level = StudentLevel.FromXp(long.MaxValue);

        var units = long.MaxValue / 50;
        var root = (long)(level - 1);
        (root * root <= units).ShouldBeTrue();
        ((root + 1) * (root + 1) > units).ShouldBeTrue();
    }

    [Fact]
    public void Large_perfect_square_boundaries_are_not_shifted_by_double_rounding()
    {
        // units = k² tam sınırında (k büyük): k² * 50 - 1 → k, k² * 50 → k + 1.
        const long k = 3_037_000_000L / 8; // k² * 50 long'a sığar
        var boundary = k * k * 50;

        StudentLevel.FromXp(boundary - 1).ShouldBe((int)k);        // isqrt(k² - 1) = k - 1 → level k
        StudentLevel.FromXp(boundary).ShouldBe((int)(k + 1));
    }

    [Fact]
    public void Level_is_monotonic_non_decreasing()
    {
        var previous = StudentLevel.FromXp(0);
        for (var xp = 1; xp <= 20_000; xp++)
        {
            var current = StudentLevel.FromXp(xp);
            current.ShouldBeGreaterThanOrEqualTo(previous);
            previous = current;
        }
    }
}
