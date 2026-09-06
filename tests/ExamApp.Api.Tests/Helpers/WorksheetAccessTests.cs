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

    // issue #11: CanView artık Public* paylaşım için owner/admin olmayan çağıranlara da true
    // döner — bu, sharing parametresinin varlık amacı. Private/Restricted (davranışı değiştirmeyen
    // dallar) için parametresiz sonuçla eşleşmeye devam eder; PublicView/PublicAssignable için
    // owner/admin olmayan durumlarda bilinçli olarak true'ya döner (aşağıdaki ayrı testler).
    public static IEnumerable<object[]> CanViewWithVisibilityCases() =>
        from c in OwnershipCases()
        from s in new[] { WorksheetTeacherSharing.Private }
        from v in Enum.GetValues<WorksheetStudentVisibility>()
        select new[] { c[0], c[1], c[2], s, v };

    [Theory]
    [MemberData(nameof(CanViewWithVisibilityCases))]
    public void CanView_PrivateSharing_MatchesParameterlessResult(
        int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing sharing, WorksheetStudentVisibility studentVisibility)
    {
        var baseline = WorksheetAccess.CanView(createUserId, userId, isAdmin);

        WorksheetAccess.CanView(createUserId, userId, isAdmin, sharing, studentVisibility).ShouldBe(baseline);
    }

    // ---- issue #11: PublicView/PublicAssignable paylaşımı herhangi bir kimliği doğrulanmış
    // öğretmene görünürlük verir; owner/admin durumunda zaten true idi, yeni davranış yalnızca
    // "sahibi/admin değil" durumunu true'ya çevirir.

    [Theory]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public void CanView_PublicSharing_NonOwnerNonAdmin_ReturnsTrue(WorksheetTeacherSharing sharing)
    {
        WorksheetAccess.CanView(createUserId: 5, userId: 9, isAdmin: false, sharing).ShouldBeTrue();
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public void CanView_PublicSharing_LegacyOwnerlessWorksheet_NonAdminReturnsFalse(WorksheetTeacherSharing sharing)
    {
        // Legacy (owner yok) worksheet Public* işaretlenmiş olsa bile admin dışında kimse görmemeli
        // — aksi halde varlığı 403 ile sızdırılır (404 dönmeli).
        WorksheetAccess.CanView(createUserId: null, userId: 9, isAdmin: false, sharing).ShouldBeFalse();
    }

    [Fact]
    public void CanView_PrivateSharing_NonOwnerNonAdmin_ReturnsFalse()
    {
        WorksheetAccess.CanView(createUserId: 5, userId: 9, isAdmin: false, WorksheetTeacherSharing.Private).ShouldBeFalse();
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public void CanView_PublicSharing_OwnerOrAdmin_StillReturnsTrue(WorksheetTeacherSharing sharing)
    {
        WorksheetAccess.CanView(createUserId: 5, userId: 5, isAdmin: false, sharing).ShouldBeTrue(); // owner
        WorksheetAccess.CanView(createUserId: 5, userId: 9, isAdmin: true, sharing).ShouldBeTrue();  // admin
    }

    [Fact]
    public void NewWorksheet_Defaults_TeacherSharingPrivateAndStudentVisibilityNormal()
    {
        var ws = new Worksheet();

        ws.TeacherSharing.ShouldBe(WorksheetTeacherSharing.Private);
        ws.StudentVisibility.ShouldBe(WorksheetStudentVisibility.Normal);
    }

    // ---- issue #14: CanStudentStartTest access matrix ----
    // (hasActiveAssignment) x (isGradeMatch) x (StudentVisibility) -> expected

    [Theory]
    [InlineData(true, true, WorksheetStudentVisibility.Normal, true)]
    [InlineData(true, true, WorksheetStudentVisibility.Restricted, true)]
    [InlineData(true, false, WorksheetStudentVisibility.Normal, true)]
    [InlineData(true, false, WorksheetStudentVisibility.Restricted, true)]
    [InlineData(false, true, WorksheetStudentVisibility.Normal, true)]
    [InlineData(false, true, WorksheetStudentVisibility.Restricted, false)]
    [InlineData(false, false, WorksheetStudentVisibility.Normal, false)]
    [InlineData(false, false, WorksheetStudentVisibility.Restricted, false)]
    public void CanStudentStartTest_AccessMatrix_ReturnsExpected(
        bool hasActiveAssignment, bool isGradeMatch, WorksheetStudentVisibility studentVisibility, bool expected)
    {
        WorksheetAccess.CanStudentStartTest(hasActiveAssignment, isGradeMatch, studentVisibility)
            .ShouldBe(expected);
    }

    // ---- issue #16: CanCopy semantiği CanView ile birebir aynı (içeride onu çağırır) ----

    public static IEnumerable<object[]> CanCopyParityCases() =>
        from c in OwnershipCases()
        from s in Enum.GetValues<WorksheetTeacherSharing>()
        from v in Enum.GetValues<WorksheetStudentVisibility>()
        select new[] { c[0], c[1], c[2], s, v };

    [Theory]
    [MemberData(nameof(CanCopyParityCases))]
    public void CanCopy_AnyInput_MatchesCanViewResult(
        int? createUserId, int userId, bool isAdmin,
        WorksheetTeacherSharing sharing, WorksheetStudentVisibility studentVisibility)
    {
        var expected = WorksheetAccess.CanView(createUserId, userId, isAdmin, sharing, studentVisibility);

        WorksheetAccess.CanCopy(createUserId, userId, isAdmin, sharing, studentVisibility).ShouldBe(expected);
    }

    [Fact]
    public void CanCopy_OwnerCopiesOwnPrivateWorksheet_ReturnsTrue()
    {
        WorksheetAccess.CanCopy(createUserId: 5, userId: 5, isAdmin: false, WorksheetTeacherSharing.Private)
            .ShouldBeTrue();
    }

    [Fact]
    public void CanCopy_StrangerOnPrivateWorksheet_ReturnsFalse()
    {
        WorksheetAccess.CanCopy(createUserId: 5, userId: 9, isAdmin: false, WorksheetTeacherSharing.Private)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public void CanCopy_StrangerOnPublicWorksheet_ReturnsTrue(WorksheetTeacherSharing sharing)
    {
        WorksheetAccess.CanCopy(createUserId: 5, userId: 9, isAdmin: false, sharing).ShouldBeTrue();
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.Private)]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public void CanCopy_Admin_ReturnsTrueForEverything(WorksheetTeacherSharing sharing)
    {
        WorksheetAccess.CanCopy(createUserId: 5, userId: 9, isAdmin: true, sharing).ShouldBeTrue();
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.PublicView)]
    [InlineData(WorksheetTeacherSharing.PublicAssignable)]
    public void CanCopy_LegacyOwnerlessPublicWorksheet_NonAdminReturnsFalse(WorksheetTeacherSharing sharing)
    {
        WorksheetAccess.CanCopy(createUserId: null, userId: 9, isAdmin: false, sharing).ShouldBeFalse();
        WorksheetAccess.CanCopy(createUserId: null, userId: 9, isAdmin: true, sharing).ShouldBeTrue();
    }
}
