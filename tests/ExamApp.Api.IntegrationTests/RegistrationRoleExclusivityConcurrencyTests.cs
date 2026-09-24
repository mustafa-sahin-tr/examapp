using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #277 (madde 9) — öğretmen + öğrenci kaydının gerçek PostgreSQL üzerinde eşzamanlı yarışı. Birim testler SQLite'ta
/// tek bağlantıyla koştuğu için yarışı yalnızca interceptor ile taklit edebilir; burada iki ayrı DI scope'u (ayrı DbContext /
/// bağlantı) aynı kullanıcı için aynı anda <c>TeacherService.Save</c> ve <c>StudentService.Save</c> çağırır.
/// <c>pg_advisory_xact_lock</c> (UserRegistrationLock) + kilit altında tekrarlanan karşı-tablo kontrolü sayesinde her turda
/// tam olarak biri başarılı olmalı, diğeri 409 (Conflict) almalı ve kullanıcının iki tabloda birden satırı OLMAMALI.
/// </summary>
public class RegistrationRoleExclusivityConcurrencyTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int Rounds = 15;

    [Fact]
    public async Task Parallel_teacher_and_student_registration_for_the_same_user_never_creates_both_rows()
    {
        var gradeId = await WithDbAsync(async db =>
        {
            var g = new Grade { Name = "7-race" };
            db.Grades.Add(g);
            await db.SaveChangesAsync();
            return g.Id;
        });

        for (var round = 0; round < Rounds; round++)
        {
            var userId = 77_000 + round;
            using var start = new SemaphoreSlim(0, 2);

            async Task<ResponseBaseDto> RegisterTeacher()
            {
                using var scope = Factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ITeacherService>();
                await start.WaitAsync();
                return await service.Save(userId, new RegisterTeacherDto());
            }

            async Task<ResponseBaseDto> RegisterStudent()
            {
                using var scope = Factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IStudentService>();
                await start.WaitAsync();
                return await service.Save(userId, new RegisterStudentDto { StudentNumber = $"n{round}", GradeId = gradeId });
            }

            var teacherTask = Task.Run(RegisterTeacher);
            var studentTask = Task.Run(RegisterStudent);
            start.Release(2);
            var results = await Task.WhenAll(teacherTask, studentTask);

            results.Count(r => r.Success).ShouldBe(1, $"round {round}: tam olarak bir kayıt kazanmalı");
            results.Single(r => !r.Success).Conflict.ShouldBeTrue($"round {round}: kaybeden 409 almalı");

            await WithDbAsync(async db =>
            {
                var teachers = await db.Teachers.CountAsync(t => t.UserId == userId);
                var students = await db.Students.CountAsync(s => s.UserId == userId);
                (teachers + students).ShouldBe(1, $"round {round}: kullanıcının yalnızca bir rol satırı olmalı");
            });
        }
    }
}
