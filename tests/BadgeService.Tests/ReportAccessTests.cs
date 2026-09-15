using BadgeService.Security;
using Shouldly;

namespace BadgeService.Tests;

/// <summary>
/// Tests for <see cref="ReportAccess.CanView"/> static method.
/// Criteria #1 (route userId vs token identity) and #2 (student sees only own data) are covered.
/// </summary>
public class ReportAccessTests
{
    [Fact]
    public void CanView_StudentViewsOwnData_ReturnsTrue()
    {
        // Criterion #2: Student can view own data
        var result = ReportAccess.CanView(callerUserId: 5, requestedUserId: 5, isAdmin: false);
        result.ShouldBeTrue();
    }

    [Fact]
    public void CanView_StudentViewsDifferentUserData_ReturnsFalse()
    {
        // Criterion #2: Student cannot view another student's data
        var result = ReportAccess.CanView(callerUserId: 5, requestedUserId: 6, isAdmin: false);
        result.ShouldBeFalse();
    }

    [Fact]
    public void CanView_UnknownCallerViewsData_ReturnsFalse()
    {
        // Criterion #1: Unidentifiable caller (null) has no access; fail-closed
        var result = ReportAccess.CanView(callerUserId: null, requestedUserId: 5, isAdmin: false);
        result.ShouldBeFalse();
    }

    [Fact]
    public void CanView_AdminViewsOtherUserData_ReturnsTrue()
    {
        // Criterion #3: Admin can view any user's data, even if not their own
        var result = ReportAccess.CanView(callerUserId: null, requestedUserId: 5, isAdmin: true);
        result.ShouldBeTrue();
    }

    [Fact]
    public void CanView_AdminWithIdentityViewsDifferentUserData_ReturnsTrue()
    {
        // Criterion #3: Admin can view any data regardless of identity resolver result
        var result = ReportAccess.CanView(callerUserId: 5, requestedUserId: 6, isAdmin: true);
        result.ShouldBeTrue();
    }
}
