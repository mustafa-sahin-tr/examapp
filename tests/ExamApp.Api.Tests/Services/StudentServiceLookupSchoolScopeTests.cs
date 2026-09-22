using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #190: GET /api/student/lookup (StudentService.GetStudentLookupsAsync) okul izolasyonu.
/// Matris: aynı okul (görür), farklı okul (listede yok), okulsuz→okullu (listede yok),
/// admin (tüm okullar). Filtre sorgu düzeyinde; auth-api ad çözümü listeyi değiştirmez.
/// issue #192: bağımsız (okulsuz) öğretmen yalnızca kendi Approved Booking'i olan öğrencileri görür
/// (Pending/Rejected/başka tutor sayılmaz); #190'daki "okulsuz → tüm okulsuz öğrenciler" ara davranışı kaldırıldı.
/// </summary>
public class StudentServiceLookupSchoolScopeTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public void Dispose() => _db.Dispose();

    private StudentService NewService(AppDbContext ctx) => new(ctx, _authApi, new SchoolAccessPolicy(ctx));

    private async Task<(int SchoolA, int SchoolB)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "Okul A" };
        var b = new School { Name = "Okul B" };
        ctx.Schools.AddRange(a, b);
        await ctx.SaveChangesAsync();

        ctx.Students.AddRange(
            new Student { UserId = 1, StudentNumber = "A1", SchoolId = a.Id },
            new Student { UserId = 2, StudentNumber = "A2", SchoolId = a.Id },
            new Student { UserId = 3, StudentNumber = "B1", SchoolId = b.Id },
            new Student { UserId = 4, StudentNumber = "N1", SchoolId = null });
        await ctx.SaveChangesAsync();

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(
                ((IEnumerable<int>)call[0]).Select(id => new UserLookupResultDto { Id = id, FullName = $"User {id}" }).ToList()));

        return (a.Id, b.Id);
    }

    [Fact]
    public async Task SameSchoolTeacher_SeesOnlyOwnSchoolStudents()
    {
        var (a, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(userId: 50, schoolId: a));

        list.Select(s => s.StudentNumber).ShouldBe(new[] { "A1", "A2" });
        list.ShouldAllBe(s => s.SchoolId == a);
        list.ShouldAllBe(s => s.FullName.StartsWith("User "));
    }

    [Fact]
    public async Task DifferentSchoolStudents_AreNotInList()
    {
        var (_, b) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(userId: 50, schoolId: b));

        list.Select(s => s.StudentNumber).ShouldBe(new[] { "B1" });
    }

    /// <summary>Bağımsız öğretmen (UserId=TutorUserId) için booking'ler: A1 Approved, B1 Pending, N1 Rejected; A2'ye başka tutor'un Approved'ı.</summary>
    private const int TutorUserId = 50;
    private const int OtherTutorUserId = 51;

    private async Task SeedBookingsAsync()
    {
        await using var ctx = _db.NewContext();
        var tutor = new Teacher { UserId = TutorUserId, SchoolId = null, IsIndependentTutor = true };
        var other = new Teacher { UserId = OtherTutorUserId, SchoolId = null, IsIndependentTutor = true };
        ctx.Teachers.AddRange(tutor, other);
        await ctx.SaveChangesAsync();

        var byNumber = await ctx.Students.ToDictionaryAsync(s => s.StudentNumber);
        BookingSeed.Add(ctx, tutor.Id, byNumber["A1"].Id, BookingStatus.Approved, 8);
        BookingSeed.Add(ctx, tutor.Id, byNumber["B1"].Id, BookingStatus.Pending, 9);
        BookingSeed.Add(ctx, tutor.Id, byNumber["N1"].Id, BookingStatus.Rejected, 10);
        BookingSeed.Add(ctx, other.Id, byNumber["A2"].Id, BookingStatus.Approved, 8);
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task IndependentTeacher_SeesOnlyStudentsWithOwnApprovedBooking()
    {
        // #190'da bu test "okulsuz → yalnızca okulsuz öğrenciler (N1)" idi; #192 ile kural Approved Booking oldu:
        // öğrencinin okulu önemsiz (A1 okullu ama görünür), okulsuz N1 Rejected olduğu için görünmez.
        await SeedAsync();
        await SeedBookingsAsync();
        await using var ctx = _db.NewContext();

        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(userId: TutorUserId, schoolId: null));

        list.Select(s => s.StudentNumber).ShouldBe(new[] { "A1" });
    }

    [Fact]
    public async Task IndependentTeacher_PendingRejectedOrOtherTutorsBookings_AreNotInList()
    {
        await SeedAsync();
        await SeedBookingsAsync();
        await using var ctx = _db.NewContext();

        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(userId: TutorUserId, schoolId: null));

        var numbers = list.Select(s => s.StudentNumber).ToList();
        numbers.ShouldNotContain("B1"); // Pending
        numbers.ShouldNotContain("N1"); // Rejected
        numbers.ShouldNotContain("A2"); // başka tutor'un Approved'ı
    }

    [Fact]
    public async Task IndependentTeacher_WithoutBookings_GetsEmptyListWithoutCallingAuthApi()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(userId: 777, schoolId: null));

        list.ShouldBeEmpty();
        await _authApi.DidNotReceive().GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IndependentTeacher_AuthApiIsAskedOnlyForApprovedBookingStudents()
    {
        await SeedAsync();
        await SeedBookingsAsync();
        await using var ctx = _db.NewContext();

        await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(userId: TutorUserId, schoolId: null));

        await _authApi.Received(1).GetUsersByIdsAsync(
            Arg.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1 })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SchoolBoundTeacher_ListIsUnchangedByBookings()
    {
        // Regresyon (#190): okullu öğretmen için kural okul eşitliği kalır; booking'ler listeyi değiştirmez.
        var (a, _) = await SeedAsync();
        await SeedBookingsAsync();
        await using var ctx = _db.NewContext();

        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(userId: 60, schoolId: a));

        list.Select(s => s.StudentNumber).ShouldBe(new[] { "A1", "A2" });
    }

    [Fact]
    public async Task Admin_SeesAllSchools()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.Unrestricted(userId: 1));

        list.Select(s => s.StudentNumber).ShouldBe(new[] { "A1", "A2", "B1", "N1" });
    }

    [Fact]
    public async Task SoftDeletedStudents_AreExcluded()
    {
        var (a, _) = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            var a2 = await setup.Students.SingleAsync(s => s.StudentNumber == "A2");
            setup.Students.Remove(a2); // BaseEntity → soft delete (IsDeleted=true)
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        (await ctx.Students.IgnoreQueryFilters().CountAsync(s => s.IsDeleted)).ShouldBe(1);

        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(50, a));

        list.Select(s => s.StudentNumber).ShouldBe(new[] { "A1" });
    }

    [Fact]
    public async Task Unrestricted_ListIsCappedAtLookupMaxTake_Deterministically()
    {
        await using (var setup = _db.NewContext())
        {
            for (var i = 0; i < StudentService.LookupMaxTake + 5; i++)
                setup.Students.Add(new Student { UserId = 1000 + i, StudentNumber = $"Z{i:D4}" });
            await setup.SaveChangesAsync();
        }
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>()));

        await using var ctx = _db.NewContext();
        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.Unrestricted(1));

        list.Count.ShouldBe(StudentService.LookupMaxTake);
        list.First().StudentNumber.ShouldBe("Z0000");
        list.Last().StudentNumber.ShouldBe($"Z{StudentService.LookupMaxTake - 1:D4}");
    }

    [Fact]
    public async Task ScopedList_AuthApiIsAskedOnlyForVisibleUsers()
    {
        var (a, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(userId: 50, schoolId: a));

        // Filtre sorgu düzeyinde: başka okulun UserId'leri auth-api'ye bile gitmez (isim sızmaz).
        await _authApi.Received(1).GetUsersByIdsAsync(
            Arg.Is<IEnumerable<int>>(ids => ids.OrderBy(i => i).SequenceEqual(new[] { 1, 2 })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyScope_ReturnsEmptyWithoutCallingAuthApi()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var list = await NewService(ctx).GetStudentLookupsAsync(SchoolScope.For(userId: 50, schoolId: 9999));

        list.ShouldBeEmpty();
        await _authApi.DidNotReceive().GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>());
    }
}
