using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1089: veli paneli çocuğun planına bakar (velinin değil); bağlantı kapısı önce gelir. Öğrencinin analiz ve çalışma
/// araçları uçları planda olmayı ister.
/// </summary>
public class ChildEntitlementsTests : IDisposable
{
    private const int ParentUserId = 90;
    private const int StudentId = 7;
    private const int StudentUserId = 91;

    private readonly TestDb _db = TestDb.Create();
    private readonly IParentChildAccess _access = Substitute.For<IParentChildAccess>();
    private readonly IParentAccessAuditLog _audit = Substitute.For<IParentAccessAuditLog>();

    public ChildEntitlementsTests() =>
        _access.EnsureActiveChildAsync(ParentUserId, StudentId, Arg.Any<CancellationToken>())
            .Returns(new ParentChildAccessGrant(1, 2, StudentId, StudentUserId, null, null));

    private async Task AssignChildAsync(string planKey)
    {
        await using var ctx = _db.NewContext();
        (await new EntitlementService(ctx, TimeProvider.System).AssignPlanAsync(StudentUserId, planKey, null, null, 1))
            .ShouldBe(AssignPlanError.None);
    }

    [Fact]
    public async Task Basic_child_has_no_analytics_and_the_parent_gets_403()
    {
        await AssignChildAsync(SubscriptionPlan.StudentBasicKey);
        await using var ctx = _db.NewContext();
        var entitlements = new EntitlementService(ctx, TimeProvider.System);

        var denied = await ChildEntitlements.DenyUnlessChildHasAsync(_access, entitlements, ParentUserId, StudentId, EntitlementFeatures.Analytics);
        var features = await ChildEntitlements.FeaturesAsync(_access, _audit, entitlements, ParentUserId, StudentId);

        denied.ShouldNotBeNull().StatusCode.ShouldBe(403);
        denied.Value.ShouldBeOfType<EntitlementLimitReachedDto>().Code.ShouldBe(EntitlementLimitReachedDto.NotInPlanCode);
        features.ShouldNotBeNull().PlanKey.ShouldBe(SubscriptionPlan.StudentBasicKey);
        features.Features[EntitlementFeatures.Analytics].ShouldBeFalse();
        features.Features[EntitlementFeatures.StudyTools].ShouldBeFalse();
        features.Features[EntitlementFeatures.Practice].ShouldBeTrue();
        features.Features.ContainsKey(EntitlementFeatures.CoachPanel).ShouldBeFalse(); // yalnız öğrenci özellikleri
    }

    [Fact]
    public async Task Pro_child_has_analytics()
    {
        await AssignChildAsync(SubscriptionPlan.StudentProKey);
        await using var ctx = _db.NewContext();
        var entitlements = new EntitlementService(ctx, TimeProvider.System);

        (await ChildEntitlements.DenyUnlessChildHasAsync(_access, entitlements, ParentUserId, StudentId, EntitlementFeatures.Analytics)).ShouldBeNull();
        (await ChildEntitlements.FeaturesAsync(_access, _audit, entitlements, ParentUserId, StudentId))!.Features[EntitlementFeatures.Analytics].ShouldBeTrue();
    }

    [Fact]
    public async Task Another_parents_child_is_left_to_the_endpoints_404()
    {
        await using var ctx = _db.NewContext();
        var entitlements = Substitute.For<IEntitlementService>();

        (await ChildEntitlements.DenyUnlessChildHasAsync(_access, entitlements, ParentUserId, 999, EntitlementFeatures.Analytics)).ShouldBeNull();
        (await ChildEntitlements.FeaturesAsync(_access, _audit, entitlements, ParentUserId, 999)).ShouldBeNull();
        await entitlements.DidNotReceiveWithAnyArgs().CheckAsync(default!, default!);
    }

    [Theory]
    [InlineData(typeof(StudentTopicPerformanceController), EntitlementFeatures.Analytics)]
    [InlineData(typeof(StudentLearningOutcomeMasteryController), EntitlementFeatures.Analytics)]
    public void Student_analytics_endpoints_need_the_feature(Type controller, string feature) =>
        controller.GetCustomAttribute<RequiresEntitlementAttribute>().ShouldNotBeNull().FeatureKey.ShouldBe(feature);

    [Theory]
    [InlineData(typeof(PracticeController), "GetSavedQuestions")]
    [InlineData(typeof(PracticeController), "SaveQuestion")]
    [InlineData(typeof(PracticeController), "GetMistakes")]
    [InlineData(typeof(PracticeController), "GetDueReview")]
    [InlineData(typeof(FlashcardsController), "CreateDeck")]
    [InlineData(typeof(FlashcardsController), "AddCard")]
    [InlineData(typeof(ProgramController), "CreateProgram")]
    public void Study_tools_endpoints_need_the_feature(Type controller, string action)
    {
        var attribute = controller.GetMethod(action)!.GetCustomAttribute<RequiresEntitlementAttribute>().ShouldNotBeNull();
        attribute.FeatureKey.ShouldBe(EntitlementFeatures.StudyTools);
        attribute.InPlanOnly.ShouldBeTrue();
    }

    [Theory]
    [InlineData(typeof(PracticeController), "UnsaveQuestion")]
    [InlineData(typeof(FlashcardsController), "DeleteDeck")]
    [InlineData(typeof(FlashcardsController), "ListDecks")]
    [InlineData(typeof(ProgramController), "GetProgramSteps")]
    public void Cleanup_and_assigned_work_stay_open(Type controller, string action) =>
        controller.GetMethod(action)!.GetCustomAttribute<RequiresEntitlementAttribute>().ShouldBeNull();

    public void Dispose() => _db.Dispose();
}
