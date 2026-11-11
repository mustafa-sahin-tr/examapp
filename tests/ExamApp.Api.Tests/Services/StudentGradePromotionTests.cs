using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.GradePromotion;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #1110: 1 Ağustos sınıf atlaması — 1–11 bir üst sınıfa, 12 yerinde; 1 Ağustos'tan sonra kaydolan atlatılmaz; ikinci tur
/// hiçbir şey yapmaz; atlama haftası dışında iş yok; admin geri alması eski sınıfa döndürür. SQLite (gerçek ilişkisel davranış).
/// </summary>
public class StudentGradePromotionTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    // 2027-08-01 03:00 İstanbul (UTC+3).
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2027, 8, 1, 0, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private sealed record World(Dictionary<int, int> GradeOfLevel);

    private async Task<World> SeedGradesAsync()
    {
        await using var ctx = _db.NewContext();
        var grades = Enumerable.Range(1, 12).Select(l => new Grade { Name = $"{l}. Sınıf", Code = $"{l}-sinif" }).ToList();
        ctx.Grades.AddRange(grades);
        ctx.Grades.Add(new Grade { Name = "Hazırlık" }); // seviyesi okunamaz: atlatılmaz
        await ctx.SaveChangesAsync();
        return new World(grades.Select((g, i) => (Level: i + 1, g.Id)).ToDictionary(x => x.Level, x => x.Id));
    }

    private async Task<int> AddStudentAsync(int userId, int? gradeId, DateTime? createdAtUtc = null)
    {
        await using var ctx = _db.NewContext();
        var student = new Student { UserId = userId, StudentNumber = $"S{userId}", SchoolName = "S", GradeId = gradeId };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        if (createdAtUtc is { } created)
            await ctx.Students.Where(s => s.Id == student.Id).ExecuteUpdateAsync(u => u.SetProperty(s => s.CreateTime, created));
        return student.Id;
    }

    private async Task<int> RunAsync()
    {
        await using var ctx = _db.NewContext();
        var calendar = new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, _time);
        return await new StudentGradePromotionJob(ctx, calendar,
            new StaticOptionsMonitor<StudentGradePromotionOptions>(new StudentGradePromotionOptions { BatchSize = 2 }), _time).RunAsync();
    }

    private async Task<int?> GradeOfAsync(int studentId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Students.Where(s => s.Id == studentId).Select(s => s.GradeId).SingleAsync();
    }

    [Fact]
    public async Task Promotes_grades_1_to_11_once_and_keeps_grade_12()
    {
        var w = await SeedGradesAsync();
        var fourth = await AddStudentAsync(1, w.GradeOfLevel[4]);
        var eighth = await AddStudentAsync(2, w.GradeOfLevel[8]);
        var eleventh = await AddStudentAsync(3, w.GradeOfLevel[11]);
        var twelfth = await AddStudentAsync(4, w.GradeOfLevel[12]);
        var noGrade = await AddStudentAsync(5, null);

        (await RunAsync()).ShouldBe(3); // parti boyutu 2: imleç bütün öğrencileri gezer

        (await GradeOfAsync(fourth)).ShouldBe(w.GradeOfLevel[5]);
        (await GradeOfAsync(eighth)).ShouldBe(w.GradeOfLevel[9]);
        (await GradeOfAsync(eleventh)).ShouldBe(w.GradeOfLevel[12]);
        (await GradeOfAsync(twelfth)).ShouldBe(w.GradeOfLevel[12]);
        (await GradeOfAsync(noGrade)).ShouldBeNull();

        _time.Now = _time.Now.AddDays(1); // telafi gecesi
        (await RunAsync()).ShouldBe(0);
        (await GradeOfAsync(fourth)).ShouldBe(w.GradeOfLevel[5]);

        await using var ctx = _db.NewContext();
        var row = await ctx.StudentGradePromotions.SingleAsync(p => p.StudentId == fourth);
        (row.Year, row.FromGradeId, row.ToGradeId).ShouldBe((2027, w.GradeOfLevel[4], w.GradeOfLevel[5]));
    }

    [Fact]
    public async Task Students_registered_after_august_first_are_not_promoted()
    {
        var w = await SeedGradesAsync();
        var late = await AddStudentAsync(1, w.GradeOfLevel[6], new DateTime(2027, 8, 1, 6, 0, 0, DateTimeKind.Utc));

        _time.Now = new DateTimeOffset(2027, 8, 2, 0, 0, 0, TimeSpan.Zero);
        (await RunAsync()).ShouldBe(0);

        (await GradeOfAsync(late)).ShouldBe(w.GradeOfLevel[6]);
    }

    [Theory]
    [InlineData(2027, 7, 31, 20)] // 31 Temmuz 23:00 İstanbul
    [InlineData(2027, 8, 7, 22)]  // 8 Ağustos 01:00 İstanbul: telafi haftası bitti
    [InlineData(2027, 12, 1, 9)]
    public async Task Does_nothing_outside_the_promotion_week(int year, int month, int day, int utcHour)
    {
        var w = await SeedGradesAsync();
        var student = await AddStudentAsync(1, w.GradeOfLevel[3], new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        _time.Now = new DateTimeOffset(year, month, day, utcHour, 0, 0, TimeSpan.Zero);
        (await RunAsync()).ShouldBe(0);

        (await GradeOfAsync(student)).ShouldBe(w.GradeOfLevel[3]);
    }

    [Fact]
    public async Task Admin_revert_moves_the_student_back_and_the_year_is_not_promoted_again()
    {
        var w = await SeedGradesAsync();
        var student = await AddStudentAsync(1, w.GradeOfLevel[7]);
        await RunAsync();

        await using (var ctx = _db.NewContext())
        {
            var service = new StudentGradePromotionService(ctx, _time);
            (await service.RevertAsync(student, actorUserId: 99)).ShouldBe(GradePromotionRevertStatus.Reverted);
            (await service.RevertAsync(student, actorUserId: 99)).ShouldBe(GradePromotionRevertStatus.NotFound);
        }
        (await GradeOfAsync(student)).ShouldBe(w.GradeOfLevel[7]);

        _time.Now = _time.Now.AddDays(1);
        (await RunAsync()).ShouldBe(0); // sınıfta kaldı: aynı yıl yeniden atlatılmaz
        (await GradeOfAsync(student)).ShouldBe(w.GradeOfLevel[7]);

        _time.Now = new DateTimeOffset(2028, 8, 1, 0, 0, 0, TimeSpan.Zero);
        (await RunAsync()).ShouldBe(1); // ertesi yıl normal
        (await GradeOfAsync(student)).ShouldBe(w.GradeOfLevel[8]);
    }

    [Fact]
    public async Task Revert_refuses_when_the_grade_changed_after_the_promotion()
    {
        var w = await SeedGradesAsync();
        var student = await AddStudentAsync(1, w.GradeOfLevel[5]);
        await RunAsync();
        await using var ctx = _db.NewContext();
        await ctx.Students.Where(s => s.Id == student).ExecuteUpdateAsync(u => u.SetProperty(s => s.GradeId, w.GradeOfLevel[9]));

        (await new StudentGradePromotionService(ctx, _time).RevertAsync(student, 99)).ShouldBe(GradePromotionRevertStatus.GradeChanged);

        (await GradeOfAsync(student)).ShouldBe(w.GradeOfLevel[9]);
        (await ctx.StudentGradePromotions.SingleAsync()).RevertedAtUtc.ShouldBeNull();
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
