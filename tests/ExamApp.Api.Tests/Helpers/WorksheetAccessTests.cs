using ExamApp.Api.Data;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

public class WorksheetAccessTests
{
    [Theory]
    [InlineData(null, 5, false, false)] // legacy null owner, normal kullanıcı
    [InlineData(0, 5, false, false)]    // legacy 0 owner, normal kullanıcı
    [InlineData(5, 5, false, true)]     // sahibi
    [InlineData(5, 9, false, false)]    // yabancı
    [InlineData(5, 9, true, true)]      // admin, sahibi değil
    [InlineData(null, 5, true, true)]   // admin, legacy kayıt
    public void CanModify_OwnershipOrAdmin_ReturnsExpected(int? createUserId, int userId, bool isAdmin, bool expected)
    {
        WorksheetAccess.CanModify(createUserId, userId, isAdmin).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, 5, false, false)]
    [InlineData(0, 5, false, false)]
    [InlineData(5, 5, false, true)]
    [InlineData(5, 9, false, false)]
    [InlineData(5, 9, true, true)]
    [InlineData(null, 5, true, true)]
    public void CanView_OwnershipOrAdmin_ReturnsExpected(int? createUserId, int userId, bool isAdmin, bool expected)
    {
        WorksheetAccess.CanView(createUserId, userId, isAdmin).ShouldBe(expected);
    }

    // ---- issue #9: yeni opsiyonel parametreler davranışı DEĞİŞTİRMEZ ----

    public static IEnumerable<object[]> OwnershipCases() => new[]
    {
        new object[] { (int?)null, 5, false },
        new object[] { (int?)0, 5, false },
        new object[] { (int?)5, 5, false },
        new object[] { (int?)5, 9, false },
        new object[] { (int?)5, 9, true },
        new object[] { (int?)null, 5, true },
    };

    public static IEnumerable<object[]> CanModifyWithSharingCases() =>
        from c in OwnershipCases()
        from s in Enum.GetValues<WorksheetTeacherSharing>()
        select new[] { c[0], c[1], c[2], s };

    [Theory]
    [MemberData(nameof(CanModifyWithSharingCases))]
    public void CanModify_WithSharingParameter_MatchesParameterlessResult(
        int? createUserId, int userId, bool isAdmin, WorksheetTeacherSharing sharing)
    {
        var baseline = WorksheetAccess.CanModify(createUserId, userId, isAdmin);

        WorksheetAccess.CanModify(createUserId, userId, isAdmin, sharing).ShouldBe(baseline);
    }

    public static IEnumerable<object[]> CanViewWithVisibilityCases() =>
        from c in OwnershipCases()
        from s in Enum.GetValues<WorksheetTeacherSharing>()
        from v in Enum.GetValues<WorksheetStudentVisibility>()
        select new[] { c[0], c[1], c[2], s, v };

    [Theory]
    [MemberData(nameof(CanViewWithVisibilityCases))]
    public void CanView_WithSharingAndStudentVisibilityParameters_MatchesParameterlessResult(
        int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing sharing, WorksheetStudentVisibility studentVisibility)
    {
        var baseline = WorksheetAccess.CanView(createUserId, userId, isAdmin);

        WorksheetAccess.CanView(createUserId, userId, isAdmin, sharing, studentVisibility).ShouldBe(baseline);
    }

    [Fact]
    public void NewWorksheet_Defaults_TeacherSharingPrivateAndStudentVisibilityNormal()
    {
        var ws = new Worksheet();

        ws.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        ws.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Normal);
    }
}
