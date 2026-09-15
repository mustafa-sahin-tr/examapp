using System.Security.Claims;
using BadgeService;
using BadgeService.Controllers;
using BadgeService.Entities;
using BadgeService.Security;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace BadgeService.Tests;

/// <summary>
/// Controller-level authorization tests for <see cref="ReportsController"/>.
/// Covers criteria #1 (route userId vs token identity), #2 (student sees only own data),
/// #3 (admin exception), and #4 (403 for unauthorized access).
/// </summary>
public class ReportsControllerAuthorizationTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    [Fact]
    public async Task GetBadgeProgressAsync_StudentRequestsOwnData_Returns200()
    {
        // Criterion #1, #2: Student requesting own userId gets own report
        var userId = 5;
        await SeedUserData(userId);

        var resolver = new TestCallerIdentityResolver(userId);
        var controller = CreateController(resolver, userIdClaim: "sub-5");

        var result = await controller.GetBadgeProgressAsync(userId, CancellationToken.None);

        result.Result.ShouldNotBeNull();
        var okResult = result.Result.ShouldBeOfType<OkObjectResult>();
        okResult.StatusCode.ShouldBe(200);

        var report = okResult.Value.ShouldNotBeNull();
        var reportDto = report.ShouldBeOfType<BadgeService.Models.BadgeProgressReportDto>();
        reportDto.Summary.ShouldNotBeNull();
        reportDto.Summary.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task GetBadgeProgressAsync_StudentRequestsDifferentUserData_Returns403()
    {
        // Criterion #2, #4: Student requesting different userId gets 403 Forbid
        var ownUserId = 5;
        var requestedUserId = 6;
        await SeedUserData(ownUserId);

        var resolver = new TestCallerIdentityResolver(ownUserId);
        var controller = CreateController(resolver, userIdClaim: "sub-5");

        var result = await controller.GetBadgeProgressAsync(requestedUserId, CancellationToken.None);

        result.Result.ShouldNotBeNull();
        result.Result.ShouldBeOfType<ForbidResult>();
        resolver.CallCount.ShouldBe(1); // Resolver was called
    }

    [Fact]
    public async Task GetBadgeProgressAsync_ResolverReturnsNull_Returns403()
    {
        // Criterion #1, #4: Unresolvable identity (resolver returns null) gets 403
        var requestedUserId = 5;
        await SeedUserData(requestedUserId);

        var resolver = new TestCallerIdentityResolver(null);
        var controller = CreateController(resolver, userIdClaim: "unknown-sub");

        var result = await controller.GetBadgeProgressAsync(requestedUserId, CancellationToken.None);

        result.Result.ShouldNotBeNull();
        result.Result.ShouldBeOfType<ForbidResult>();
        resolver.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetBadgeProgressAsync_AdminRequestsDifferentUserData_Returns200()
    {
        // Criterion #3: Admin can view any user's data; resolver not called
        var requestedUserId = 6;
        await SeedUserData(requestedUserId);

        var resolver = new TestCallerIdentityResolver(null); // Should not be called
        var controller = CreateController(resolver, userIdClaim: "admin-sub", isAdmin: true);

        var result = await controller.GetBadgeProgressAsync(requestedUserId, CancellationToken.None);

        result.Result.ShouldNotBeNull();
        var okResult = result.Result.ShouldBeOfType<OkObjectResult>();
        okResult.StatusCode.ShouldBe(200);

        resolver.CallCount.ShouldBe(0); // Admin bypass; resolver not invoked
    }

    [Fact]
    public async Task GetActivityAsync_StudentRequestsOwnData_Returns200()
    {
        // Criterion #1, #2: Student requesting own userId gets own activity report
        var userId = 5;
        await SeedUserActivity(userId);

        var resolver = new TestCallerIdentityResolver(userId);
        var controller = CreateController(resolver, userIdClaim: "sub-5");

        var result = await controller.GetActivityAsync(userId, null, null, CancellationToken.None);

        result.Result.ShouldNotBeNull();
        var okResult = result.Result.ShouldBeOfType<OkObjectResult>();
        okResult.StatusCode.ShouldBe(200);

        var report = okResult.Value.ShouldNotBeNull();
        var reportDto = report.ShouldBeOfType<BadgeService.Models.ActivityReportDto>();
        reportDto.UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task GetActivityAsync_StudentRequestsDifferentUserData_Returns403()
    {
        // Criterion #2, #4: Student requesting different userId gets 403 Forbid
        var ownUserId = 5;
        var requestedUserId = 6;
        await SeedUserActivity(ownUserId);

        var resolver = new TestCallerIdentityResolver(ownUserId);
        var controller = CreateController(resolver, userIdClaim: "sub-5");

        var result = await controller.GetActivityAsync(requestedUserId, null, null, CancellationToken.None);

        result.Result.ShouldNotBeNull();
        result.Result.ShouldBeOfType<ForbidResult>();
        resolver.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetActivityAsync_ResolverReturnsNull_Returns403()
    {
        // Criterion #1, #4: Unresolvable identity (resolver returns null) gets 403
        var requestedUserId = 5;
        await SeedUserActivity(requestedUserId);

        var resolver = new TestCallerIdentityResolver(null);
        var controller = CreateController(resolver, userIdClaim: "unknown-sub");

        var result = await controller.GetActivityAsync(requestedUserId, null, null, CancellationToken.None);

        result.Result.ShouldNotBeNull();
        result.Result.ShouldBeOfType<ForbidResult>();
        resolver.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetActivityAsync_AdminRequestsDifferentUserData_Returns200()
    {
        // Criterion #3: Admin can view any user's activity; resolver not called
        var requestedUserId = 6;
        await SeedUserActivity(requestedUserId);

        var resolver = new TestCallerIdentityResolver(null);
        var controller = CreateController(resolver, userIdClaim: "admin-sub", isAdmin: true);

        var result = await controller.GetActivityAsync(requestedUserId, null, null, CancellationToken.None);

        result.Result.ShouldNotBeNull();
        var okResult = result.Result.ShouldBeOfType<OkObjectResult>();
        okResult.StatusCode.ShouldBe(200);

        resolver.CallCount.ShouldBe(0);
    }

    private ReportsController CreateController(
        ICallerIdentityResolver resolver,
        string userIdClaim,
        bool isAdmin = false)
    {
        var ctx = _db.NewContext();
        var reportService = new StudentReportService(ctx);
        var logger = new TestLogger<ReportsController>();

        var controller = new ReportsController(reportService, resolver, logger);

        // Set up HttpContext and User principal
        var identity = new ClaimsIdentity("Bearer");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userIdClaim));
        if (isAdmin)
            identity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));

        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return controller;
    }

    private async Task SeedUserData(int userId)
    {
        await using var ctx = _db.NewContext();
        ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TotalQuestions = 10,
            CorrectQuestions = 7,
            TotalPoints = 70,
            BestCorrectStreak = 3,
        });
        var badgeDefId = Guid.NewGuid();
        ctx.BadgeDefinitions.Add(new BadgeDefinition
        {
            Id = badgeDefId,
            Name = "Test Badge",
            Description = "Test",
            Category = "Test",
            RuleType = "AnswerCount",
            RuleConfigJson = "{\"target\":10}",
        });
        await ctx.SaveChangesAsync();
    }

    private async Task SeedUserActivity(int userId)
    {
        await using var ctx = _db.NewContext();
        ctx.StudentDailyActivities.Add(new StudentDailyActivity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ActivityDate = DateTime.UtcNow.Date,
            QuestionCount = 5,
        });
        ctx.StudentQuestionAggregates.Add(new StudentQuestionAggregate
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TotalQuestions = 5,
            CorrectQuestions = 3,
            TotalPoints = 30,
            BestCorrectStreak = 2,
        });
        await ctx.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();

    private sealed class TestCallerIdentityResolver : ICallerIdentityResolver
    {
        private readonly int? _resolvedUserId;
        public int CallCount { get; private set; }

        public TestCallerIdentityResolver(int? resolvedUserId) => _resolvedUserId = resolvedUserId;

        public Task<int?> ResolveUserIdAsync(HttpContext httpContext, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_resolvedUserId);
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
