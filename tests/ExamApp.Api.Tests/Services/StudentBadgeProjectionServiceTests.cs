using ExamApp.Api.Consumers;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Badges;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #422: <see cref="StudentBadgeEarnedEvent"/> → <c>StudentBadgeProjections</c>. Idempotent (öğrenci + rozet UNIQUE),
/// öğrenci yoksa / payload geçersizse no-op, ad/ikon uzunluk sınırı, UTC; consumer servis hatasını yeniden fırlatır (retry).
/// </summary>
public class StudentBadgeProjectionServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private static readonly Guid BadgeA = Guid.NewGuid();
    private static readonly Guid BadgeB = Guid.NewGuid();

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedStudentAsync(int userId = 42)
    {
        await using var ctx = _db.NewContext();
        var s = new Student { UserId = userId, StudentNumber = $"S{userId}" };
        ctx.Students.Add(s);
        await ctx.SaveChangesAsync();
        return s.Id;
    }

    private static StudentBadgeEarnedEvent Event(Guid badgeId, string name = "İlk Adım", string? icon = "rocket_launch", int userId = 42,
        DateTime? earnedAt = null) => new()
    {
        UserId = userId, BadgeDefinitionId = badgeId, Name = name, Icon = icon,
        EarnedAtUtc = earnedAt ?? new DateTime(2026, 10, 5, 22, 30, 0, DateTimeKind.Utc)
    };

    private async Task<StudentBadgeProjectionResult> ApplyAsync(StudentBadgeEarnedEvent evt)
    {
        await using var ctx = _db.NewContext();
        return await new StudentBadgeProjectionService(ctx, NullLogger<StudentBadgeProjectionService>.Instance).ApplyAsync(evt);
    }

    private async Task<List<StudentBadgeProjection>> RowsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.StudentBadgeProjections.AsNoTracking().OrderBy(b => b.Id).ToListAsync();
    }

    [Fact]
    public async Task Earned_badge_is_projected_once_and_redelivery_is_a_no_op()
    {
        var studentId = await SeedStudentAsync();

        (await ApplyAsync(Event(BadgeA))).ShouldBe(StudentBadgeProjectionResult.Applied);
        (await ApplyAsync(Event(BadgeA, name: "Yeniden adlandırıldı"))).ShouldBe(StudentBadgeProjectionResult.Duplicate);
        (await ApplyAsync(Event(BadgeB, icon: null))).ShouldBe(StudentBadgeProjectionResult.Applied);

        var rows = await RowsAsync();
        rows.Select(r => (r.StudentId, r.BadgeDefinitionId, r.Name, r.Icon)).ShouldBe(new[]
        {
            (studentId, BadgeA, "İlk Adım", (string?)"rocket_launch"),
            (studentId, BadgeB, "İlk Adım", (string?)null)
        });
        rows[0].EarnedAtUtc.ShouldBe(new DateTime(2026, 10, 5, 22, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Unknown_student_and_invalid_payload_are_skipped()
    {
        await SeedStudentAsync();

        (await ApplyAsync(Event(BadgeA, userId: 999))).ShouldBe(StudentBadgeProjectionResult.StudentNotFound);
        (await ApplyAsync(Event(Guid.Empty))).ShouldBe(StudentBadgeProjectionResult.Invalid);
        (await ApplyAsync(Event(BadgeA, name: "  "))).ShouldBe(StudentBadgeProjectionResult.Invalid);
        (await RowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Long_names_are_truncated_and_unspecified_kind_is_treated_as_utc()
    {
        await SeedStudentAsync();
        await ApplyAsync(Event(BadgeA, name: new string('a', 500),
            earnedAt: new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Unspecified)));

        var row = (await RowsAsync()).Single();
        row.Name.Length.ShouldBe(200);
        row.EarnedAtUtc.ShouldBe(new DateTime(2026, 10, 5, 8, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData("rocket_launch", "rocket_launch")]
    [InlineData("not_in_the_list", null)]
    [InlineData("Rocket_Launch", null)]
    [InlineData("<img src=x>", null)]
    [InlineData("", null)]
    public async Task Icon_is_stored_only_when_allowlisted(string icon, string? expected)
    {
        await SeedStudentAsync();
        await ApplyAsync(Event(BadgeA, icon: icon));
        (await RowsAsync()).Single().Icon.ShouldBe(expected);
    }

    [Fact]
    public async Task Late_event_for_a_badge_earned_before_the_last_reset_is_dropped()
    {
        var studentId = await SeedStudentAsync();
        var resetAt = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        await using (var ctx = _db.NewContext())
            await ctx.Students.Where(s => s.Id == studentId)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.ProgressResetAtUtc, (DateTime?)resetAt));

        (await ApplyAsync(Event(BadgeA, earnedAt: resetAt.AddMinutes(-6)))).ShouldBe(StudentBadgeProjectionResult.BeforeReset);
        // 5 dk tolerans içi (saat farkı) ve sonrası kabul edilir.
        (await ApplyAsync(Event(BadgeB, earnedAt: resetAt.AddMinutes(-4)))).ShouldBe(StudentBadgeProjectionResult.Applied);
        (await RowsAsync()).Select(r => r.BadgeDefinitionId).ShouldBe(new[] { BadgeB });
    }

    [Fact]
    public async Task Consumer_rethrows_unexpected_errors_for_retry()
    {
        var service = Substitute.For<IStudentBadgeProjectionService>();
        service.ApplyAsync(Arg.Any<StudentBadgeEarnedEvent>(), Arg.Any<CancellationToken>())
            .Returns<Task<StudentBadgeProjectionResult>>(_ => throw new InvalidOperationException("db down"));
        var consumer = new StudentBadgeEarnedConsumer(service, NullLogger<StudentBadgeEarnedConsumer>.Instance);
        var context = Substitute.For<ConsumeContext<StudentBadgeEarnedEvent>>();
        context.Message.Returns(Event(BadgeA));

        await Should.ThrowAsync<InvalidOperationException>(() => consumer.Consume(context));
    }
}
