using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Tutors;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #95: bağımsız öğretmen tutor profili (kendi profil okuma/yazma) ve öğrenci araması.
/// Kabul kriterleri:
/// - Onaylı bağımsız öğretmen dersleri/ücreti/ders şeklini düzenleyebiliyor.
/// - Öğrenci branş/ders bazlı arama yapabiliyor.
/// - Arama sonuçlarında sadece ApprovalStatus=Approved öğretmenler görünüyor.
/// - Profilde dersler/online-yüzyüze/ücret gösteriliyor.
/// </summary>
public class TeacherServiceTutorProfileTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    private TeacherService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private static Teacher IndependentApproved(int userId, string? bio = null) => new()
    {
        UserId = userId,
        IsIndependentTutor = true,
        ApprovalStatus = TeacherApprovalStatus.Approved,
        Bio = bio
    };

    private static Teacher IndependentPending(int userId) => new()
    {
        UserId = userId,
        IsIndependentTutor = true,
        ApprovalStatus = TeacherApprovalStatus.Pending
    };

    private static Teacher SchoolBound(int userId) => new()
    {
        UserId = userId,
        IsIndependentTutor = false,
        ApprovalStatus = TeacherApprovalStatus.Approved
    };

    // ---- GetTutorProfileAsync ----

    [Fact]
    public async Task GetTutorProfileAsync_ValidIndependentTutor_ReturnsTutorProfile()
    {
        int teacherId, subjectId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subjectId = subject.Id;

            var teacher = IndependentApproved(userId: 1, bio: "Deneyimli öğretmen");
            teacher.HourlyRate = 150;
            teacher.TeachesOnline = true;
            teacher.TeachesInPerson = false;
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;

            teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subjectId });
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetTutorProfileAsync(userId: 1);

        result.Success.ShouldBeTrue();
        result.Profile.ShouldNotBeNull();
        result.Profile.TeacherId.ShouldBe(teacherId);
        result.Profile.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
        result.Profile.HourlyRate.ShouldBe(150);
        result.Profile.TeachesOnline.ShouldBeTrue();
        result.Profile.TeachesInPerson.ShouldBeFalse();
        result.Profile.Bio.ShouldBe("Deneyimli öğretmen");
        result.Profile.Subjects.ShouldHaveSingleItem();
        result.Profile.Subjects[0].Name.ShouldBe("Matematik");
    }

    [Fact]
    public async Task GetTutorProfileAsync_TeacherNotFound_ReturnsNotFound()
    {
        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetTutorProfileAsync(userId: 9999);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
        result.Message.ShouldContain("bulunamadı");
    }

    [Fact]
    public async Task GetTutorProfileAsync_NonIndependentTeacher_ReturnsForbidden()
    {
        await using (var ctxSetup = _db.NewContext())
        {
            ctxSetup.Teachers.Add(SchoolBound(userId: 2));
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetTutorProfileAsync(userId: 2);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
        result.Message.ShouldContain("bağımsız");
    }

    [Fact]
    public async Task GetTutorProfileAsync_PendingIndependentTeacher_ReturnsProfil()
    {
        int teacherId;
        await using (var ctxSetup = _db.NewContext())
        {
            var teacher = IndependentPending(userId: 3);
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetTutorProfileAsync(userId: 3);

        result.Success.ShouldBeTrue();
        result.Profile.ShouldNotBeNull();
        result.Profile.TeacherId.ShouldBe(teacherId);
        result.Profile.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    [Fact]
    public async Task GetTutorProfileAsync_MultipleSubjects_ReturnsSortedByName()
    {
        int teacherId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subjectZ = new Subject { Name = "Zooloji" };
            var subjectA = new Subject { Name = "Algebra" };
            ctxSetup.Subjects.AddRange(subjectZ, subjectA);
            await ctxSetup.SaveChangesAsync();

            var teacher = IndependentApproved(userId: 4);
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;

            teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subjectZ.Id });
            teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subjectA.Id });
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetTutorProfileAsync(userId: 4);

        result.Profile.Subjects.Count.ShouldBe(2);
        result.Profile.Subjects[0].Name.ShouldBe("Algebra");
        result.Profile.Subjects[1].Name.ShouldBe("Zooloji");
    }

    // ---- UpdateTutorProfileAsync ----

    [Fact]
    public async Task UpdateTutorProfileAsync_ValidUpdate_UpdatesFieldsAndSubjects()
    {
        int teacherId;
        int subject1Id, subject2Id;
        await using (var ctxSetup = _db.NewContext())
        {
            var s1 = new Subject { Name = "Matematik" };
            var s2 = new Subject { Name = "Fizik" };
            ctxSetup.Subjects.AddRange(s1, s2);
            await ctxSetup.SaveChangesAsync();
            subject1Id = s1.Id;
            subject2Id = s2.Id;

            var teacher = IndependentApproved(userId: 10);
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var ctxTest = _db.NewContext();
        var dto = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { subject1Id, subject2Id },
            HourlyRate = 200,
            TeachesOnline = true,
            TeachesInPerson = true,
            Bio = "Tecrübeli"
        };

        var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 10, dto);

        result.Success.ShouldBeTrue();
        result.Profile.ShouldNotBeNull();
        result.Profile.HourlyRate.ShouldBe(200);
        result.Profile.TeachesOnline.ShouldBeTrue();
        result.Profile.TeachesInPerson.ShouldBeTrue();
        result.Profile.Bio.ShouldBe("Tecrübeli");
        result.Profile.Subjects.Count.ShouldBe(2);
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_EmptySubjectIds_FailsValidation()
    {
        await using (var ctxSetup = _db.NewContext())
        {
            ctxSetup.Teachers.Add(IndependentApproved(userId: 11));
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var dto = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int>(),
            HourlyRate = 100,
            TeachesOnline = true,
            TeachesInPerson = false
        };

        var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 11, dto);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("ders");
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_BothTeachingModeFalse_FailsValidation()
    {
        int subject1Id;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subject1Id = subject.Id;

            ctxSetup.Teachers.Add(IndependentApproved(userId: 12));
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var dto = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { subject1Id },
            HourlyRate = 100,
            TeachesOnline = false,
            TeachesInPerson = false
        };

        var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 12, dto);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("şekli");
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_ZeroOrNegativeRate_FailsValidation()
    {
        int subject1Id;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subject1Id = subject.Id;

            ctxSetup.Teachers.Add(IndependentApproved(userId: 13));
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var dto = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { subject1Id },
            HourlyRate = 0,
            TeachesOnline = true,
            TeachesInPerson = false
        };

        var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 13, dto);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("ücret");
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_InvalidSubjectId_FailsValidation()
    {
        await using (var ctxSetup = _db.NewContext())
        {
            ctxSetup.Teachers.Add(IndependentApproved(userId: 14));
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var dto = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { 9999, 10000 },
            HourlyRate = 100,
            TeachesOnline = true,
            TeachesInPerson = false
        };

        var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 14, dto);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("Geçersiz");
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_TeacherNotFound_ReturnsNotFound()
    {
        int subject1Id;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subject1Id = subject.Id;
        }

        await using var ctxTest = _db.NewContext();
        var dto = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { subject1Id },
            HourlyRate = 100,
            TeachesOnline = true,
            TeachesInPerson = false
        };

        var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 9999, dto);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_NonIndependentTeacher_ReturnsForbidden()
    {
        int subject1Id;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subject1Id = subject.Id;

            ctxSetup.Teachers.Add(SchoolBound(userId: 15));
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var dto = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { subject1Id },
            HourlyRate = 100,
            TeachesOnline = true,
            TeachesInPerson = false
        };

        var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 15, dto);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_ReplaceSubjects_RemovesOldAddsNew()
    {
        int teacherId, subject1Id, subject2Id, subject3Id;
        await using (var ctxSetup = _db.NewContext())
        {
            var s1 = new Subject { Name = "Matematik" };
            var s2 = new Subject { Name = "Fizik" };
            var s3 = new Subject { Name = "Kimya" };
            ctxSetup.Subjects.AddRange(s1, s2, s3);
            await ctxSetup.SaveChangesAsync();
            subject1Id = s1.Id;
            subject2Id = s2.Id;
            subject3Id = s3.Id;

            var teacher = IndependentApproved(userId: 16);
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;

            // İlk olarak 1 ve 2 ekle
            teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subject1Id });
            teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subject2Id });
            await ctxSetup.SaveChangesAsync();
        }

        await using (var ctxTest = _db.NewContext())
        {
            // 2 ve 3 ile güncelle (1 silinir, 3 eklenir)
            var dto = new UpdateTutorProfileDto
            {
                SubjectIds = new List<int> { subject2Id, subject3Id },
                HourlyRate = 100,
                TeachesOnline = true,
                TeachesInPerson = false
            };

            var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 16, dto);
            result.Success.ShouldBeTrue();
        }

        // Verify
        await using var ctxVerify = _db.NewContext();
        var reloadedTeacher = await ctxVerify.Teachers
            .Include(t => t.TeacherSubjects)
            .SingleAsync(t => t.Id == teacherId);

        reloadedTeacher.TeacherSubjects.Count.ShouldBe(2);
        var subjectIds = reloadedTeacher.TeacherSubjects.Select(ts => ts.SubjectId).ToHashSet();
        subjectIds.ShouldContain(subject2Id);
        subjectIds.ShouldContain(subject3Id);
        subjectIds.ShouldNotContain(subject1Id);
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_SameSubjectsIdempotent_SucceedsWithoutError()
    {
        int teacherId, subject1Id;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subject1Id = subject.Id;

            var teacher = IndependentApproved(userId: 17);
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;

            teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subject1Id });
            await ctxSetup.SaveChangesAsync();
        }

        // Aynı subject ID'ler ile iki kez update yap
        await using (var ctxTest1 = _db.NewContext())
        {
            var dto = new UpdateTutorProfileDto
            {
                SubjectIds = new List<int> { subject1Id },
                HourlyRate = 100,
                TeachesOnline = true,
                TeachesInPerson = false
            };

            var result1 = await NewService(ctxTest1).UpdateTutorProfileAsync(userId: 17, dto);
            result1.Success.ShouldBeTrue();
        }

        await using (var ctxTest2 = _db.NewContext())
        {
            var dto = new UpdateTutorProfileDto
            {
                SubjectIds = new List<int> { subject1Id },
                HourlyRate = 100,
                TeachesOnline = true,
                TeachesInPerson = false
            };

            var result2 = await NewService(ctxTest2).UpdateTutorProfileAsync(userId: 17, dto);
            result2.Success.ShouldBeTrue(); // İkinci çağrı da başarılı
        }

        // Verify: still only 1 subject (no duplicates due to unique index)
        await using var ctxVerify = _db.NewContext();
        var reloadedTeacher = await ctxVerify.Teachers
            .Include(t => t.TeacherSubjects)
            .SingleAsync(t => t.Id == teacherId);
        reloadedTeacher.TeacherSubjects.Count.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_NullBio_StoresNull()
    {
        int subject1Id;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subject1Id = subject.Id;

            var teacher = IndependentApproved(userId: 18, bio: "Eski bio");
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var dto = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { subject1Id },
            HourlyRate = 100,
            TeachesOnline = true,
            TeachesInPerson = false,
            Bio = null
        };

        var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 18, dto);
        result.Success.ShouldBeTrue();
        result.Profile!.Bio.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateTutorProfileAsync_WhitespaceBio_StoresNull()
    {
        int subject1Id;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subject1Id = subject.Id;

            var teacher = IndependentApproved(userId: 19, bio: "Eski bio");
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var dto = new UpdateTutorProfileDto
        {
            SubjectIds = new List<int> { subject1Id },
            HourlyRate = 100,
            TeachesOnline = true,
            TeachesInPerson = false,
            Bio = "   "
        };

        var result = await NewService(ctxTest).UpdateTutorProfileAsync(userId: 19, dto);
        result.Success.ShouldBeTrue();
        result.Profile!.Bio.ShouldBeNull();
    }

    // ---- SearchTutorsAsync ----

    [Fact]
    public async Task SearchTutorsAsync_OnlyApprovedIndependentTeachers_Returned()
    {
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();

            // Onaylı bağımsız
            var t1 = IndependentApproved(userId: 20);
            t1.HourlyRate = 100;
            t1.TeachesOnline = true;
            ctxSetup.Teachers.Add(t1);

            // Beklemede bağımsız
            var t2 = IndependentPending(userId: 21);
            t2.HourlyRate = 100;
            t2.TeachesOnline = true;
            ctxSetup.Teachers.Add(t2);

            // Okula bağlı, onaylı
            var t3 = SchoolBound(userId: 22);
            t3.HourlyRate = 100;
            t3.TeachesOnline = true;
            ctxSetup.Teachers.Add(t3);

            // Reddedilen bağımsız
            var t4 = new Teacher
            {
                UserId = 23,
                IsIndependentTutor = true,
                ApprovalStatus = TeacherApprovalStatus.Rejected,
                HourlyRate = 100,
                TeachesOnline = true
            };
            ctxSetup.Teachers.Add(t4);

            await ctxSetup.SaveChangesAsync();

            // Tümüne subject ekle
            foreach (var teacher in ctxSetup.Teachers.Where(t => t.IsIndependentTutor))
            {
                teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subject.Id });
            }
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var results = await NewService(ctxTest).SearchTutorsAsync(new TeacherSearchFilterDto());

        results.Count.ShouldBe(1);
        await using var ctxVerify = _db.NewContext();
        results[0].TeacherId.ShouldBe((await ctxVerify.Teachers.SingleAsync(t => t.UserId == 20)).Id);
    }

    [Fact]
    public async Task SearchTutorsAsync_SubjectFilter_ReturnsOnlyThatSubject()
    {
        int mathSubjectId, physicsSubjectId;
        await using (var ctxSetup = _db.NewContext())
        {
            var math = new Subject { Name = "Matematik" };
            var physics = new Subject { Name = "Fizik" };
            ctxSetup.Subjects.AddRange(math, physics);
            await ctxSetup.SaveChangesAsync();
            mathSubjectId = math.Id;
            physicsSubjectId = physics.Id;

            // Matematik öğretmeni
            var t1 = IndependentApproved(userId: 30);
            t1.HourlyRate = 100;
            t1.TeachesOnline = true;
            ctxSetup.Teachers.Add(t1);

            // Fizik öğretmeni
            var t2 = IndependentApproved(userId: 31);
            t2.HourlyRate = 100;
            t2.TeachesOnline = true;
            ctxSetup.Teachers.Add(t2);

            await ctxSetup.SaveChangesAsync();

            t1.TeacherSubjects.Add(new TeacherSubject { TeacherId = t1.Id, SubjectId = mathSubjectId });
            t2.TeacherSubjects.Add(new TeacherSubject { TeacherId = t2.Id, SubjectId = physicsSubjectId });
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var filter = new TeacherSearchFilterDto { SubjectId = physicsSubjectId };
        var results = await NewService(ctxTest).SearchTutorsAsync(filter);

        results.Count.ShouldBe(1);
        results[0].Subjects.ShouldHaveSingleItem().SubjectId.ShouldBe(physicsSubjectId);
    }

    [Fact]
    public async Task SearchTutorsAsync_PriceFilter_ReturnsInRange()
    {
        int subjectId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subjectId = subject.Id;

            var t1 = IndependentApproved(userId: 40);
            t1.HourlyRate = 50;
            t1.TeachesOnline = true;
            ctxSetup.Teachers.Add(t1);

            var t2 = IndependentApproved(userId: 41);
            t2.HourlyRate = 150;
            t2.TeachesOnline = true;
            ctxSetup.Teachers.Add(t2);

            var t3 = IndependentApproved(userId: 42);
            t3.HourlyRate = 250;
            t3.TeachesOnline = true;
            ctxSetup.Teachers.Add(t3);

            await ctxSetup.SaveChangesAsync();

            foreach (var t in new[] { t1, t2, t3 })
                t.TeacherSubjects.Add(new TeacherSubject { TeacherId = t.Id, SubjectId = subjectId });
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var filter = new TeacherSearchFilterDto { MinPrice = 100, MaxPrice = 200 };
        var results = await NewService(ctxTest).SearchTutorsAsync(filter);

        results.Count.ShouldBe(1);
        results[0].HourlyRate.ShouldBe(150);
    }

    [Fact]
    public async Task SearchTutorsAsync_OnlineFilter_ReturnsOnlyOnline()
    {
        int subjectId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subjectId = subject.Id;

            var t1 = IndependentApproved(userId: 50);
            t1.HourlyRate = 100;
            t1.TeachesOnline = true;
            t1.TeachesInPerson = false;
            ctxSetup.Teachers.Add(t1);

            var t2 = IndependentApproved(userId: 51);
            t2.HourlyRate = 100;
            t2.TeachesOnline = false;
            t2.TeachesInPerson = true;
            ctxSetup.Teachers.Add(t2);

            await ctxSetup.SaveChangesAsync();

            t1.TeacherSubjects.Add(new TeacherSubject { TeacherId = t1.Id, SubjectId = subjectId });
            t2.TeacherSubjects.Add(new TeacherSubject { TeacherId = t2.Id, SubjectId = subjectId });
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var filter = new TeacherSearchFilterDto { Online = true };
        var results = await NewService(ctxTest).SearchTutorsAsync(filter);

        results.Count.ShouldBe(1);
        results[0].TeachesOnline.ShouldBeTrue();
        results[0].TeachesInPerson.ShouldBeFalse();
    }

    [Fact]
    public async Task SearchTutorsAsync_CombinedFilters_AppliesAll()
    {
        int mathId, physicsId;
        await using (var ctxSetup = _db.NewContext())
        {
            var math = new Subject { Name = "Matematik" };
            var physics = new Subject { Name = "Fizik" };
            ctxSetup.Subjects.AddRange(math, physics);
            await ctxSetup.SaveChangesAsync();
            mathId = math.Id;
            physicsId = physics.Id;

            // Match all criteria
            var t1 = IndependentApproved(userId: 70);
            t1.HourlyRate = 150;
            t1.TeachesOnline = true;
            t1.TeachesInPerson = false;
            ctxSetup.Teachers.Add(t1);

            // Wrong subject
            var t2 = IndependentApproved(userId: 71);
            t2.HourlyRate = 150;
            t2.TeachesOnline = true;
            t2.TeachesInPerson = false;
            ctxSetup.Teachers.Add(t2);

            // Wrong price
            var t3 = IndependentApproved(userId: 72);
            t3.HourlyRate = 500;
            t3.TeachesOnline = true;
            t3.TeachesInPerson = false;
            ctxSetup.Teachers.Add(t3);

            await ctxSetup.SaveChangesAsync();

            t1.TeacherSubjects.Add(new TeacherSubject { TeacherId = t1.Id, SubjectId = mathId });
            t2.TeacherSubjects.Add(new TeacherSubject { TeacherId = t2.Id, SubjectId = physicsId });
            t3.TeacherSubjects.Add(new TeacherSubject { TeacherId = t3.Id, SubjectId = mathId });
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var filter = new TeacherSearchFilterDto
        {
            SubjectId = mathId,
            MinPrice = 100,
            MaxPrice = 200,
            Online = true
        };
        var results = await NewService(ctxTest).SearchTutorsAsync(filter);

        results.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SearchTutorsAsync_Pagination_SkipAndTake()
    {
        int subjectId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subjectId = subject.Id;

            for (int i = 0; i < 10; i++)
            {
                var t = IndependentApproved(userId: 80 + i);
                t.HourlyRate = 100 + i * 10;
                t.TeachesOnline = true;
                ctxSetup.Teachers.Add(t);
            }
            await ctxSetup.SaveChangesAsync();

            foreach (var t in ctxSetup.Teachers.Where(x => x.UserId >= 80 && x.UserId < 90))
            {
                t.TeacherSubjects.Add(new TeacherSubject { TeacherId = t.Id, SubjectId = subjectId });
            }
            await ctxSetup.SaveChangesAsync();
        }

        await using var ctxTest = _db.NewContext();
        var filter1 = new TeacherSearchFilterDto { Skip = 0, Take = 5 };
        var results1 = await NewService(ctxTest).SearchTutorsAsync(filter1);
        results1.Count.ShouldBe(5);

        var filter2 = new TeacherSearchFilterDto { Skip = 5, Take = 5 };
        var results2 = await NewService(ctxTest).SearchTutorsAsync(filter2);
        results2.Count.ShouldBe(5);

        results1[0].HourlyRate.ShouldNotBe(results2[0].HourlyRate);
    }

    [Fact]
    public async Task SearchTutorsAsync_AuthApiException_ReturnsFallbackName()
    {
        int subjectId, teacherId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subjectId = subject.Id;

            var t = IndependentApproved(userId: 110);
            t.HourlyRate = 100;
            t.TeachesOnline = true;
            ctxSetup.Teachers.Add(t);
            await ctxSetup.SaveChangesAsync();
            teacherId = t.Id;

            t.TeacherSubjects.Add(new TeacherSubject { TeacherId = t.Id, SubjectId = subjectId });
            await ctxSetup.SaveChangesAsync();
        }

        // Mock auth-api failure
        _authApi.GetUsersByIdsAsync(Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromException<IReadOnlyList<UserLookupResultDto>>(new HttpRequestException("Connection failed")));

        await using var ctxTest = _db.NewContext();
        var results = await NewService(ctxTest).SearchTutorsAsync(new TeacherSearchFilterDto());

        results.Count.ShouldBe(1);
        results[0].FullName.ShouldBe($"Öğretmen #{teacherId}");
    }

    // ---- GetPublicProfileAsync ----

    [Fact]
    public async Task GetPublicProfileAsync_ApprovedIndependentTeacher_ReturnsProfile()
    {
        int teacherId, subjectId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();
            subjectId = subject.Id;

            var teacher = IndependentApproved(userId: 120, bio: "Tam metin bio");
            teacher.HourlyRate = 200;
            teacher.TeachesOnline = true;
            teacher.TeachesInPerson = false;
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;

            teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subjectId });
            await ctxSetup.SaveChangesAsync();
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(new List<UserLookupResultDto>
            {
                new UserLookupResultDto { Id = 120, FullName = "Ahmet Öğretmen", Avatar = "http://avatar.url" }
            }));

        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetPublicProfileAsync(teacherId);

        result.ShouldNotBeNull();
        result!.TeacherId.ShouldBe(teacherId);
        result.FullName.ShouldBe("Ahmet Öğretmen");
        result.Avatar.ShouldBe("http://avatar.url");
        result.HourlyRate.ShouldBe(200);
        result.Bio.ShouldBe("Tam metin bio");
    }

    [Fact]
    public async Task GetPublicProfileAsync_PendingIndependentTeacher_ReturnsNull()
    {
        int teacherId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();

            var teacher = IndependentPending(userId: 121);
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetPublicProfileAsync(teacherId);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPublicProfileAsync_SchoolBoundTeacher_ReturnsNull()
    {
        int teacherId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();

            var teacher = SchoolBound(userId: 122);
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetPublicProfileAsync(teacherId);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPublicProfileAsync_NonexistentTeacher_ReturnsNull()
    {
        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetPublicProfileAsync(teacherId: 9999);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPublicProfileAsync_RejectedIndependentTeacher_ReturnsNull()
    {
        int teacherId;
        await using (var ctxSetup = _db.NewContext())
        {
            var teacher = new Teacher
            {
                UserId = 123,
                IsIndependentTutor = true,
                ApprovalStatus = TeacherApprovalStatus.Rejected
            };
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetPublicProfileAsync(teacherId);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetPublicProfileAsync_AuthApiException_ReturnsFallback()
    {
        int teacherId;
        await using (var ctxSetup = _db.NewContext())
        {
            var subject = new Subject { Name = "Matematik" };
            ctxSetup.Subjects.Add(subject);
            await ctxSetup.SaveChangesAsync();

            var teacher = IndependentApproved(userId: 124);
            teacher.HourlyRate = 100;
            ctxSetup.Teachers.Add(teacher);
            await ctxSetup.SaveChangesAsync();
            teacherId = teacher.Id;

            teacher.TeacherSubjects.Add(new TeacherSubject { TeacherId = teacher.Id, SubjectId = subject.Id });
            await ctxSetup.SaveChangesAsync();
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IReadOnlyList<int>>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromException<IReadOnlyList<UserLookupResultDto>>(new HttpRequestException("Failed")));

        await using var ctxTest = _db.NewContext();
        var result = await NewService(ctxTest).GetPublicProfileAsync(teacherId);

        result.ShouldNotBeNull();
        result!.FullName.ShouldBe($"Öğretmen #{teacherId}");
        result.Avatar.ShouldBe(string.Empty);
    }

    public void Dispose() => _db.Dispose();
}
