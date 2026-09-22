using ExamApp.Api.Services.Tenancy;

namespace ExamApp.Api.Tests.Services;

/// <summary>issue #190: SchoolScope değer nesnesi — fabrika metotları ve IsIndependent semantiği.</summary>
public class SchoolScopeTests
{
    [Fact]
    public void For_WithNullSchool_IsIndependent()
    {
        var scope = SchoolScope.For(1, null);

        scope.IsIndependent.ShouldBeTrue();
        scope.IsUnrestricted.ShouldBeFalse();
        scope.SchoolId.ShouldBeNull();
        scope.UserId.ShouldBe(1);
    }

    [Fact]
    public void For_WithSchool_IsNeitherIndependentNorUnrestricted()
    {
        var scope = SchoolScope.For(1, 10);

        scope.IsIndependent.ShouldBeFalse();
        scope.IsUnrestricted.ShouldBeFalse();
        scope.SchoolId.ShouldBe(10);
    }

    [Fact]
    public void Unrestricted_IsNeverIndependent_AndHasNoSchool()
    {
        var scope = SchoolScope.Unrestricted(7);

        scope.IsUnrestricted.ShouldBeTrue();
        scope.IsIndependent.ShouldBeFalse();
        scope.SchoolId.ShouldBeNull();
        scope.UserId.ShouldBe(7);
    }

    [Fact]
    public void ValueEquality_HoldsForSameInputs()
        => SchoolScope.For(1, 10).ShouldBe(SchoolScope.For(1, 10));
}
