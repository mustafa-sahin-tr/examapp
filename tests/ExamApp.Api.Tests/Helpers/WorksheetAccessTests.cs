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
}
