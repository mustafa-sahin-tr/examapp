using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Tutors;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #418: tutor pazar yeri (arama + public profil) randevu servisleriyle aynı bağımsızlık kuralını kullanır
/// (<c>TeacherIndependence</c>: <c>IsIndependentTutor &amp;&amp; SchoolId == null</c>). Okula bağlı ve hibrit (bağımsız
/// başvurulu ama okula bağlı) öğretmen aramada listelenmez, public profili bilinmeyen öğretmenle aynı null'ı (404) döner.
/// Her testte aynı tutor okul atamasından önce görünür — filtre yalnız okul bağından kaynaklanır.
/// </summary>
public class TutorMarketplaceIndependenceTests : IDisposable
{
    private const int TutorUserId = 310;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public TutorMarketplaceIndependenceTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new() { Id = TutorUserId, FullName = "Tutor", KeycloakId = "kc-tutor" }
            }));
    }

    public void Dispose() => _db.Dispose();

    private TeacherService Teachers(AppDbContext ctx) => new(ctx, _authApi);

    private async Task<int> SeedTutorAsync()
    {
        await using var ctx = _db.NewContext();
        var subject = new Subject { Name = "Matematik" };
        ctx.Subjects.Add(subject);
        var tutor = new Teacher
        {
            UserId = TutorUserId, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved,
            AccountApprovedAt = DateTime.UtcNow, HourlyRate = 100, TeachesOnline = true, Bio = "bio"
        };
        ctx.Teachers.Add(tutor);
        await ctx.SaveChangesAsync();
        tutor.TeacherSubjects.Add(new TeacherSubject { TeacherId = tutor.Id, SubjectId = subject.Id });
        await ctx.SaveChangesAsync();
        return tutor.Id;
    }

    /// <param name="keepIndependentFlag">true: hibrit (bağımsız başvuru korunur); false: düz okula bağlı öğretmen.</param>
    private async Task BindToSchoolAsync(int teacherId, bool keepIndependentFlag)
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Okul" };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        await ctx.Teachers.Where(t => t.Id == teacherId).ExecuteUpdateAsync(s => s
            .SetProperty(t => t.SchoolId, school.Id)
            .SetProperty(t => t.IsIndependentTutor, keepIndependentFlag));
    }

    [Theory]
    [InlineData(true)]  // hibrit
    [InlineData(false)] // okula bağlı
    public async Task Search_does_not_list_a_school_bound_or_hybrid_teacher(bool hybrid)
    {
        var tutorId = await SeedTutorAsync();
        await using (var ctx = _db.NewContext())
            (await Teachers(ctx).SearchTutorsAsync(new TeacherSearchFilterDto())).Select(r => r.TeacherId).ShouldBe([tutorId]);

        await BindToSchoolAsync(tutorId, keepIndependentFlag: hybrid);

        await using var after = _db.NewContext();
        (await Teachers(after).SearchTutorsAsync(new TeacherSearchFilterDto())).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]  // hibrit
    [InlineData(false)] // okula bağlı
    public async Task Public_profile_of_a_school_bound_or_hybrid_teacher_is_not_found(bool hybrid)
    {
        var tutorId = await SeedTutorAsync();
        await using (var ctx = _db.NewContext())
            (await Teachers(ctx).GetPublicProfileAsync(tutorId)).ShouldNotBeNull();

        await BindToSchoolAsync(tutorId, keepIndependentFlag: hybrid);

        await using var after = _db.NewContext();
        (await Teachers(after).GetPublicProfileAsync(tutorId)).ShouldBeNull(); // controller → 404, bilinmeyenle aynı
        (await Teachers(after).GetPublicProfileAsync(987654)).ShouldBeNull();
    }
}
