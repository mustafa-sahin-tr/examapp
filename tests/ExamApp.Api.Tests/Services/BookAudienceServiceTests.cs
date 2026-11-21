using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Labeling;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1259: kitabın erişim listesi — öğretmen kaydı kimliğiyle gelir, kullanıcı kimliğiyle tutulur; bütün olarak değişir.</summary>
public class BookAudienceServiceTests
{
    [Fact]
    public async Task Replace_keeps_teacher_user_ids_returns_teacher_record_ids_and_rejects_unknown_members()
    {
        using var db = TestDb.Create();
        int bookId, schoolId, teacherId;
        await using (var seed = db.NewContext())
        {
            var book = new Book { Name = "V1259 Kitap" };
            var school = new School { Name = "Okul A" };
            seed.AddRange(book, school);
            await seed.SaveChangesAsync();
            var teacher = new Teacher { UserId = 4242, SchoolId = school.Id };
            seed.Teachers.Add(teacher);
            seed.BookAudienceMembers.Add(new BookAudienceMember { BookId = book.Id, SchoolId = school.Id });
            await seed.SaveChangesAsync();
            (bookId, schoolId, teacherId) = (book.Id, school.Id, teacher.Id);
        }

        await using var ctx = db.NewContext();
        var service = new BookAudienceService(ctx);

        var audience = await service.ReplaceAsync(bookId, new BookAudienceSaveDto { TeacherIds = [teacherId, teacherId] });

        audience!.Schools.ShouldBeEmpty();
        audience.Teachers.ShouldBe([new BookAudienceTeacherDto(teacherId, null, "Okul A")]);
        (await ctx.BookAudienceMembers.SingleAsync()).TeacherUserId.ShouldBe(4242);

        await Should.ThrowAsync<LabelingValidationException>(() =>
            service.ReplaceAsync(bookId, new BookAudienceSaveDto { SchoolIds = [schoolId + 99] }));
        (await service.ValidateAsync(new BookAudienceSaveDto())).ShouldBe("labeling.books.audienceEmpty");
        (await service.ReplaceAsync(bookId + 99, new BookAudienceSaveDto())).ShouldBeNull();
        (await service.ReplaceAsync(bookId, new BookAudienceSaveDto()))!.Teachers.ShouldBeEmpty();
    }
}
