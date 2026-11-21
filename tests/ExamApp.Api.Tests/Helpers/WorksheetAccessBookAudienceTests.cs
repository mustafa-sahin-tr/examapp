using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// GitHub issue #1259 — WorksheetTeacherSharing.BookAudience: kitabın erişim listesindeki öğretmen ve listedeki okulların
/// öğretmenleri için PublicAssignable ile aynı; diğerleri için Private. Liste kararı DB'de (ResolveBookAudienceAsync, liste
/// sorgusunda VisibleToTeacherPredicate alt sorgusu).
/// </summary>
public class WorksheetAccessBookAudienceTests
{
    private const int Owner = 5;
    private const int Listed = 7;
    private const int SameSchool = 8;
    private const int Stranger = 9;

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void BookAudience_NonOwner_DependsOnTheList(bool inBookAudience, bool expected)
    {
        WorksheetAccess.CanView(Owner, Stranger, isAdmin: false, WorksheetTeacherSharing.BookAudience, inBookAudience: inBookAudience)
            .ShouldBe(expected);
        WorksheetAccess.CanAssign(Owner, Stranger, isAdmin: false, WorksheetTeacherSharing.BookAudience, inBookAudience: inBookAudience)
            .ShouldBe(expected);
        WorksheetAccess.CanCopy(Owner, Stranger, isAdmin: false, WorksheetTeacherSharing.BookAudience, inBookAudience: inBookAudience)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.Private)]
    [InlineData(WorksheetTeacherSharing.SchoolOnly)]
    public void List_membership_opens_only_BookAudience_tests(WorksheetTeacherSharing sharing)
    {
        WorksheetAccess.CanView(Owner, Stranger, isAdmin: false, sharing, inBookAudience: true).ShouldBeFalse();
        WorksheetAccess.CanAssign(Owner, Stranger, isAdmin: false, sharing, inBookAudience: true).ShouldBeFalse();
    }

    [Fact]
    public void BookAudience_owner_and_admin_always_see_it_and_an_ownerless_one_stays_hidden()
    {
        WorksheetAccess.CanView(Owner, Owner, isAdmin: false, WorksheetTeacherSharing.BookAudience).ShouldBeTrue();
        WorksheetAccess.CanAssign(Owner, Stranger, isAdmin: true, WorksheetTeacherSharing.BookAudience).ShouldBeTrue();
        WorksheetAccess.CanView(null, Stranger, isAdmin: false, WorksheetTeacherSharing.BookAudience, inBookAudience: true).ShouldBeFalse();
    }

    [Fact]
    public async Task Listed_teachers_and_teachers_of_listed_schools_see_and_resolve_the_book_tests_others_do_not()
    {
        using var db = TestDb.Create();
        int listedSchool, otherSchool, audienceTest, privateTest, booklessTest;
        await using (var seed = db.NewContext())
        {
            var a = new School { Name = "Okul A" };
            var b = new School { Name = "Okul B" };
            var book = new Book { Name = "V1259 Kitap" };
            seed.AddRange(a, b, book);
            await seed.SaveChangesAsync();
            (listedSchool, otherSchool) = (a.Id, b.Id);
            var bookTest = new BookTest { BookId = book.Id, Name = "1-01" };
            seed.BookTests.Add(bookTest);
            seed.Teachers.AddRange(
                new Teacher { UserId = Listed, SchoolId = otherSchool },
                new Teacher { UserId = SameSchool, SchoolId = listedSchool },
                new Teacher { UserId = Stranger, SchoolId = otherSchool });
            seed.BookAudienceMembers.AddRange(
                new BookAudienceMember { BookId = book.Id, SchoolId = listedSchool },
                new BookAudienceMember { BookId = book.Id, TeacherUserId = Listed });
            await seed.SaveChangesAsync();
            var grade = new Grade { Name = "5" };
            seed.Grades.Add(grade);
            await seed.SaveChangesAsync();
            Worksheet Test(string name, int? bookTestId, WorksheetTeacherSharing sharing) =>
                new() { Name = name, Description = "d", GradeId = grade.Id, BookTestId = bookTestId, TeacherSharing = sharing };
            var tests = new[]
            {
                Test("aud", bookTest.Id, WorksheetTeacherSharing.BookAudience),
                Test("priv", bookTest.Id, WorksheetTeacherSharing.Private),
                Test("nobook", null, WorksheetTeacherSharing.BookAudience),
            };
            seed.Worksheets.AddRange(tests);
            await seed.SaveChangesAsync();
            // Kayıt sırasında CreateUserId oturumdaki kullanıcıdan yazılır; sahip sonradan atanır.
            foreach (var test in tests)
                test.CreateUserId = Owner;
            await seed.SaveChangesAsync();
            (audienceTest, privateTest, booklessTest) = (tests[0].Id, tests[1].Id, tests[2].Id);
        }

        await using var ctx = db.NewContext();
        async Task<int[]> Visible(int userId, int? schoolId) => await ctx.Worksheets
            .Where(WorksheetAccess.VisibleToTeacherPredicate(ctx, userId, schoolId))
            .Select(w => w.Id).OrderBy(id => id).ToArrayAsync();

        (await Visible(Listed, otherSchool)).ShouldBe([audienceTest]);
        (await Visible(Listed, null)).ShouldBe([audienceTest]);
        (await Visible(SameSchool, listedSchool)).ShouldBe([audienceTest]);
        (await Visible(Stranger, otherSchool)).ShouldBeEmpty();
        (await Visible(Owner, null)).ShouldBe([audienceTest, privateTest, booklessTest]);

        var worksheet = await ctx.Worksheets.SingleAsync(w => w.Id == audienceTest);
        (await ctx.ResolveBookAudienceAsync(worksheet, Listed, isAdmin: false)).ShouldBeTrue();
        (await ctx.ResolveBookAudienceAsync(worksheet, SameSchool, isAdmin: false)).ShouldBeTrue();
        (await ctx.ResolveBookAudienceAsync(worksheet, Stranger, isAdmin: false)).ShouldBeFalse();
        (await ctx.ResolveBookAudienceAsync(await ctx.Worksheets.SingleAsync(w => w.Id == booklessTest), Listed, isAdmin: false))
            .ShouldBeFalse();
    }
}
