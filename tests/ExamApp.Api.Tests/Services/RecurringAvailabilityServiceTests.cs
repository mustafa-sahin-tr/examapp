using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #178 — Tekrarlayan haftalık müsaitlik kuralı: materialize (90 gün ufuk), çakışan hafta
/// atlama, EffectiveUntil, idempotent top-up, soft-delete edilmiş occurrence'ın yeniden doğmaması,
/// tüm seri silmede randevulu slotun korunması, sahiplik ve doğrulama.
/// </summary>
public class RecurringAvailabilityServiceTests : IDisposable
{
    private const int TeacherId = 10;
    private const int TeacherUserId = 100;
    private const int OtherTeacherId = 11;
    private const int OtherTeacherUserId = 101;
    private const int StudentId = 20;
    private const int StudentUserId = 200;

    /// <summary>Sabit "şimdi": Pazartesi 2026-01-05 08:00 UTC. Ufuk = 2026-04-05.</summary>
    private static readonly DateTime FixedNow = new(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedNow);

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly IVideoSessionProvider _videoProvider = Substitute.For<IVideoSessionProvider>();

    private static RecurringAvailabilityService NewService(AppDbContext ctx, DateTime? now = null) =>
        new(ctx, new FakeTimeProvider(now ?? FixedNow), NullLogger<RecurringAvailabilityService>.Instance);

    private BookingService NewBookingService(AppDbContext ctx, DateTime? now = null)
    {
        var tp = new FakeTimeProvider(now ?? FixedNow);
        var recurring = new RecurringAvailabilityService(ctx, tp, NullLogger<RecurringAvailabilityService>.Instance);
        return new BookingService(ctx, _authApi, _videoProvider, Options.Create(new VideoOptions()), tp, recurring,
            NullLogger<BookingService>.Instance, new ExamApp.Api.Services.Tenancy.SchoolAccessPolicy(ctx));
    }

    private static CreateRecurringAvailabilityRuleDto WednesdayRule(DateOnly? effectiveFrom = null, DateOnly? effectiveUntil = null) => new()
    {
        DayOfWeek = DayOfWeek.Wednesday,
        StartTime = new TimeOnly(14, 0),
        EndTime = new TimeOnly(15, 0),
        EffectiveFrom = effectiveFrom ?? Today,
        EffectiveUntil = effectiveUntil
    };

    private async Task SeedTeacherAsync(int teacherId, int userId, TeacherApprovalStatus status = TeacherApprovalStatus.Approved)
    {
        await using var ctx = _db.NewContext();
        ctx.Teachers.Add(new Teacher { Id = teacherId, UserId = userId, ApprovalStatus = status, Bio = "test" });
        await ctx.SaveChangesAsync();
    }

    private async Task SeedStudentAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.Students.Add(new Student { Id = StudentId, UserId = StudentUserId, StudentNumber = $"STU{StudentId}" });
        await ctx.SaveChangesAsync();
    }

    private async Task<int> SeedSlotAsync(int teacherId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(teacherId);
        var slot = new TeacherAvailabilitySlot { TeacherId = teacherId, Date = date, StartTime = start, EndTime = end, CreatedAt = FixedNow };
        ctx.TeacherAvailabilitySlots.Add(slot);
        await ctx.SaveChangesAsync();
        return slot.Id;
    }

    private async Task SeedBookingAsync(int slotId)
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(StudentUserId);
        ctx.Bookings.Add(new Booking
        {
            TeacherId = TeacherId,
            StudentId = StudentId,
            AvailabilitySlotId = slotId,
            Status = BookingStatus.Pending,
            CreatedAt = FixedNow
        });
        await ctx.SaveChangesAsync();
    }

    private async Task<RecurringAvailabilityRuleResultDto> CreateRuleAsync(CreateRecurringAvailabilityRuleDto dto, int userId = TeacherUserId)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CreateRuleAsync(userId, dto);
    }

    /// <summary>Kurala ait silinmemiş slot tarihleri (sıralı).</summary>
    private async Task<List<DateOnly>> RuleSlotDatesAsync(int ruleId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TeacherAvailabilitySlots
            .Where(s => s.RecurringAvailabilityRuleId == ruleId)
            .OrderBy(s => s.Date)
            .Select(s => s.Date)
            .ToListAsync();
    }

    // ------ Kural oluşturma / materialize ------

    [Fact]
    public async Task CreateRuleAsync_ValidRule_GeneratesWeeklySlotsUntilHorizon()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        var result = await CreateRuleAsync(WednesdayRule());

        result.Success.ShouldBeTrue();
        result.Rule.ShouldNotBeNull();
        result.Rule!.IsActive.ShouldBeTrue();
        result.Rule.EffectiveUntil.ShouldBeNull();
        result.SkippedDates.ShouldBeEmpty();

        // 2026-01-07 .. 2026-04-01 arası her Çarşamba = 13 hafta (ufuk 2026-04-05, sonraki Çarşamba 04-08 dışarıda).
        var dates = await RuleSlotDatesAsync(result.Rule.Id);
        dates.Count.ShouldBe(13);
        result.GeneratedSlotIds.Count.ShouldBe(13);
        dates.First().ShouldBe(new DateOnly(2026, 1, 7));
        dates.Last().ShouldBe(new DateOnly(2026, 4, 1));
        dates.ShouldAllBe(d => d.DayOfWeek == DayOfWeek.Wednesday);
        dates.Zip(dates.Skip(1)).ShouldAllBe(p => p.Second.DayNumber - p.First.DayNumber == 7);

        await using var ctx = _db.NewContext();
        var slots = await ctx.TeacherAvailabilitySlots.Where(s => s.RecurringAvailabilityRuleId == result.Rule.Id).ToListAsync();
        slots.ShouldAllBe(s => s.TeacherId == TeacherId && s.StartTime == new TimeOnly(14, 0) && s.EndTime == new TimeOnly(15, 0));
    }

    [Fact]
    public async Task CreateRuleAsync_EffectiveUntil_LimitsGeneratedWeeks()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        var result = await CreateRuleAsync(WednesdayRule(effectiveUntil: new DateOnly(2026, 1, 28)));

        result.Success.ShouldBeTrue();
        var dates = await RuleSlotDatesAsync(result.Rule!.Id);
        dates.ShouldBe(new[]
        {
            new DateOnly(2026, 1, 7), new DateOnly(2026, 1, 14), new DateOnly(2026, 1, 21), new DateOnly(2026, 1, 28)
        });
    }

    [Fact]
    public async Task CreateRuleAsync_EffectiveFromInFuture_StartsFromFirstMatchingDay()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        // EffectiveFrom Perşembe 2026-01-15 → ilk Çarşamba 2026-01-21.
        var result = await CreateRuleAsync(WednesdayRule(effectiveFrom: new DateOnly(2026, 1, 15)));

        result.Success.ShouldBeTrue();
        var dates = await RuleSlotDatesAsync(result.Rule!.Id);
        dates.First().ShouldBe(new DateOnly(2026, 1, 21));
        dates.Count.ShouldBe(11);
    }

    [Fact]
    public async Task CreateRuleAsync_OverlappingExistingSlot_SkipsThatWeekAndReportsIt()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var clash = new DateOnly(2026, 1, 14);
        await SeedSlotAsync(TeacherId, clash, new TimeOnly(14, 30), new TimeOnly(15, 30));

        var result = await CreateRuleAsync(WednesdayRule());

        result.Success.ShouldBeTrue();
        result.SkippedDates.ShouldBe(new[] { clash });
        result.GeneratedSlotIds.Count.ShouldBe(12);
        var dates = await RuleSlotDatesAsync(result.Rule!.Id);
        dates.ShouldNotContain(clash);
        dates.Count.ShouldBe(12);
    }

    [Fact]
    public async Task CreateRuleAsync_TodayOccurrenceAlreadyStarted_IsNotGenerated()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        // Bugün Pazartesi 08:00; 07:00-08:00 aralığı geçmiş → ilk üretim gelecek hafta.
        var dto = new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(7, 0),
            EndTime = new TimeOnly(8, 0),
            EffectiveFrom = Today
        };

        var result = await CreateRuleAsync(dto);

        result.Success.ShouldBeTrue();
        var dates = await RuleSlotDatesAsync(result.Rule!.Id);
        dates.First().ShouldBe(new DateOnly(2026, 1, 12));
    }

    [Fact]
    public async Task CreateRuleAsync_TeacherNotApproved_FailsWithForbidden()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId, TeacherApprovalStatus.Pending);

        var result = await CreateRuleAsync(WednesdayRule());

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateRuleAsync_NoTeacherRecord_FailsWithNotFound()
    {
        var result = await CreateRuleAsync(WednesdayRule());

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateRuleAsync_DuplicateActiveRule_FailsWithConflict()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        (await CreateRuleAsync(WednesdayRule())).Success.ShouldBeTrue();

        var result = await CreateRuleAsync(WednesdayRule());

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateRuleAsync_OverlappingActiveRuleSameDay_FailsWithConflict()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        (await CreateRuleAsync(WednesdayRule())).Success.ShouldBeTrue(); // 14:00-15:00

        var result = await CreateRuleAsync(new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 30), EndTime = new(15, 30), EffectiveFrom = Today
        });

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        await using var ctx = _db.NewContext();
        (await ctx.RecurringAvailabilityRules.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CreateRuleAsync_AdjacentRuleSameDay_Succeeds()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        (await CreateRuleAsync(WednesdayRule())).Success.ShouldBeTrue(); // 14:00-15:00

        var result = await CreateRuleAsync(new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Wednesday, StartTime = new(15, 0), EndTime = new(16, 0), EffectiveFrom = Today
        });

        result.Success.ShouldBeTrue();
        result.SkippedDates.ShouldBeEmpty();
        result.GeneratedSlotIds.Count.ShouldBe(13);
    }

    [Fact]
    public async Task CreateRuleAsync_SameTimeNonOverlappingDateRanges_Succeeds()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        (await CreateRuleAsync(WednesdayRule(effectiveUntil: new DateOnly(2026, 1, 31)))).Success.ShouldBeTrue();

        var result = await CreateRuleAsync(WednesdayRule(effectiveFrom: new DateOnly(2026, 2, 1)));

        result.Success.ShouldBeTrue();
        result.SkippedDates.ShouldBeEmpty();
        (await RuleSlotDatesAsync(result.Rule!.Id)).First().ShouldBe(new DateOnly(2026, 2, 4));
    }

    [Fact]
    public async Task CreateRuleAsync_SameTimeOverlappingDateRanges_FailsWithConflict()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        (await CreateRuleAsync(WednesdayRule(effectiveUntil: new DateOnly(2026, 2, 4)))).Success.ShouldBeTrue();

        // Yeni kural 2026-02-04'te başlıyor: eskisinin son günüyle kesişir.
        var result = await CreateRuleAsync(WednesdayRule(effectiveFrom: new DateOnly(2026, 2, 4)));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateRuleAsync_MaxActiveRulesReached_FailsWithValidationError()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await using (var ctx = _db.NewContext())
        {
            // 50 aktif kural: 7 gün x 8 saat dilimi (08:00..15:00) → ilk 50'si.
            var rules = Enumerable.Range(0, RecurringAvailabilityService.MaxActiveRulesPerTeacher)
                .Select(i => new RecurringAvailabilityRule
                {
                    TeacherId = TeacherId,
                    DayOfWeek = (DayOfWeek)(i % 7),
                    StartTime = new TimeOnly(8 + i / 7, 0),
                    EndTime = new TimeOnly(9 + i / 7, 0),
                    EffectiveFrom = Today,
                    IsActive = true
                });
            ctx.RecurringAvailabilityRules.AddRange(rules);
            await ctx.SaveChangesAsync();
        }

        var result = await CreateRuleAsync(new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Wednesday, StartTime = new(20, 0), EndTime = new(21, 0), EffectiveFrom = Today
        });

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeFalse();
        result.NotFound.ShouldBeFalse();
        result.Forbidden.ShouldBeFalse();
        result.Message.ShouldContain(RecurringAvailabilityService.MaxActiveRulesPerTeacher.ToString());
    }

    public static TheoryData<string, CreateRecurringAvailabilityRuleDto> InvalidRules => new()
    {
        { "endBeforeStart", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(15, 0), EndTime = new(14, 0), EffectiveFrom = Today } },
        { "endEqualsStart", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 0), EndTime = new(14, 0), EffectiveFrom = Today } },
        { "tooLong", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(10, 0), EndTime = new(14, 1), EffectiveFrom = Today } },
        { "tooShort", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 0), EndTime = new(14, 29), EffectiveFrom = Today } },
        { "invalidPrecisionStart", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 0, 30), EndTime = new(15, 0), EffectiveFrom = Today } },
        { "invalidPrecisionEnd", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 0), EndTime = new(15, 0, 0, 1), EffectiveFrom = Today } },
        { "effectiveFromInPast", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 0), EndTime = new(15, 0), EffectiveFrom = Today.AddDays(-1) } },
        { "effectiveFromTooFar", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 0), EndTime = new(15, 0), EffectiveFrom = Today.AddDays(91) } },
        { "effectiveUntilBeforeFrom", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 0), EndTime = new(15, 0), EffectiveFrom = Today.AddDays(5), EffectiveUntil = Today.AddDays(4) } },
        { "effectiveUntilEqualsFrom", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 0), EndTime = new(15, 0), EffectiveFrom = Today.AddDays(5), EffectiveUntil = Today.AddDays(5) } },
        { "untilTooFar", new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new(14, 0), EndTime = new(15, 0), EffectiveFrom = Today, EffectiveUntil = Today.AddYears(1).AddDays(1) } },
        { "invalidDayOfWeek", new() { DayOfWeek = (DayOfWeek)7, StartTime = new(14, 0), EndTime = new(15, 0), EffectiveFrom = Today } }
    };

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public async Task CreateRuleAsync_InvalidInput_FailsWithValidationError(string _, CreateRecurringAvailabilityRuleDto dto)
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        var result = await CreateRuleAsync(dto);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeFalse();
        result.Forbidden.ShouldBeFalse();
        result.Conflict.ShouldBeFalse();

        await using var ctx = _db.NewContext();
        (await ctx.RecurringAvailabilityRules.CountAsync()).ShouldBe(0);
        (await ctx.TeacherAvailabilitySlots.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CreateRuleAsync_ExactlyAtLimits_Succeeds()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        // Tam 4 saat, EffectiveFrom tam 90 gün ileri, EffectiveUntil tam 1 yıl sonra.
        var result = await CreateRuleAsync(new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Wednesday, StartTime = new(10, 0), EndTime = new(14, 0),
            EffectiveFrom = Today.AddDays(90), EffectiveUntil = Today.AddDays(90).AddYears(1)
        });

        result.Success.ShouldBeTrue();

        // Tam 30 dakika da kabul.
        (await CreateRuleAsync(new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Monday, StartTime = new(10, 0), EndTime = new(10, 30), EffectiveFrom = Today
        })).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateRuleAsync_TodayOccurrenceNotYetStarted_IsGenerated()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        // Bugün Pazartesi 08:00; 09:00-10:00 henüz başlamadı → bugün üretilir.
        var result = await CreateRuleAsync(new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Monday, StartTime = new(9, 0), EndTime = new(10, 0), EffectiveFrom = Today
        });

        result.Success.ShouldBeTrue();
        (await RuleSlotDatesAsync(result.Rule!.Id)).First().ShouldBe(Today);
    }

    [Fact]
    public async Task CreateRuleAsync_TodayOccurrenceStartsExactlyNow_IsNotGenerated()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        // Sınır: StartTime == now (08:00) → "<=" ile geçmiş sayılır.
        var result = await CreateRuleAsync(new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Monday, StartTime = new(8, 0), EndTime = new(9, 0), EffectiveFrom = Today
        });

        result.Success.ShouldBeTrue();
        (await RuleSlotDatesAsync(result.Rule!.Id)).First().ShouldBe(Today.AddDays(7));
    }

    [Fact]
    public async Task CreateRuleAsync_EffectiveFromOnRuleDay_FirstOccurrenceIsThatDay()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        var result = await CreateRuleAsync(WednesdayRule(effectiveFrom: new DateOnly(2026, 1, 7)));

        result.Success.ShouldBeTrue();
        (await RuleSlotDatesAsync(result.Rule!.Id)).First().ShouldBe(new DateOnly(2026, 1, 7));
    }

    [Fact]
    public async Task CreateRuleAsync_EffectiveUntilBeyondHorizon_HorizonCuts()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        var result = await CreateRuleAsync(WednesdayRule(effectiveUntil: new DateOnly(2026, 12, 31)));

        result.Success.ShouldBeTrue();
        var dates = await RuleSlotDatesAsync(result.Rule!.Id);
        dates.Count.ShouldBe(13);
        dates.Last().ShouldBe(new DateOnly(2026, 4, 1));
    }

    // ------ Top-up ------

    [Fact]
    public async Task TopUpAsync_CalledTwiceWithoutTimePassing_IsIdempotent()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var created = await CreateRuleAsync(WednesdayRule());

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);
        (await svc.TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(0);
        (await svc.TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(0);

        (await RuleSlotDatesAsync(created.Rule!.Id)).Count.ShouldBe(13);
    }

    [Fact]
    public async Task TopUpAsync_AfterTimeAdvances_AddsOnlyNewWeeksAndStaysIdempotent()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var created = await CreateRuleAsync(WednesdayRule());

        // 30 gün sonra: yeni ufuk 2026-05-05 → eklenen Çarşambalar 04-08, 04-15, 04-22, 04-29 = 4.
        var later = FixedNow.AddDays(30);
        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx, later).TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(4);
        }

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx, later).TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(0);
        }

        var dates = await RuleSlotDatesAsync(created.Rule!.Id);
        dates.Count.ShouldBe(17);
        dates.Distinct().Count().ShouldBe(17);
        dates.Last().ShouldBe(new DateOnly(2026, 4, 29));
    }

    [Fact]
    public async Task TopUpAsync_SoftDeletedOccurrence_IsNotRecreated()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var created = await CreateRuleAsync(WednesdayRule());
        var target = new DateOnly(2026, 1, 21);

        int slotId;
        await using (var ctx = _db.NewContext())
        {
            slotId = await ctx.TeacherAvailabilitySlots
                .Where(s => s.RecurringAvailabilityRuleId == created.Rule!.Id && s.Date == target)
                .Select(s => s.Id)
                .SingleAsync();
        }

        // "Sadece bu hafta" = mevcut DeleteSlotAsync.
        await using (var ctx = _db.NewContext())
        {
            (await NewBookingService(ctx).DeleteSlotAsync(TeacherUserId, slotId)).Success.ShouldBeTrue();
        }

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(0);
        }

        (await RuleSlotDatesAsync(created.Rule!.Id)).ShouldNotContain(target);
    }

    [Fact]
    public async Task TopUpAsync_RespectsEffectiveUntil()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var created = await CreateRuleAsync(WednesdayRule(effectiveUntil: new DateOnly(2026, 2, 28)));

        await using var ctx = _db.NewContext();
        (await NewService(ctx, FixedNow.AddDays(60)).TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(0);
        (await RuleSlotDatesAsync(created.Rule!.Id)).Last().ShouldBe(new DateOnly(2026, 2, 25));
    }

    [Fact]
    public async Task TopUpAsync_TeacherWithoutRules_ReturnsZeroAndWritesNothing()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        (await NewService(ctx).TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(0);
        (await ctx.TeacherAvailabilitySlots.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task TopUpAsync_ConcurrentWriterWinsUniqueIndex_SwallowsAndReturnsZero()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var created = await CreateRuleAsync(WednesdayRule());
        var later = FixedNow.AddDays(30);
        var raceDate = new DateOnly(2026, 4, 8); // top-up'ın ekleyeceği ilk yeni hafta

        // "Sorgu → kaydet" arasına giren eşzamanlı yazıcı: SavingChanges anında aynı occurrence'ı
        // ikinci bir context ile ekler → unique index (TeacherId, Date, Start, End) ihlali.
        var interceptor = new ConcurrentInsertInterceptor(async () =>
        {
            await SeedSlotAsync(TeacherId, raceDate, new TimeOnly(14, 0), new TimeOnly(15, 0));
        });

        await using var ctx = _db.NewContext(interceptor);
        var added = await NewService(ctx, later).TopUpAsync(TeacherId, TeacherUserId);

        added.ShouldBe(0);
        interceptor.Fired.ShouldBeTrue();
        ctx.ChangeTracker.Entries<TeacherAvailabilitySlot>().ShouldBeEmpty(); // eklenenler detach edildi

        // Bir sonraki (normal) çağrı: yarışılan tarih çakışma olarak atlanır, kalan 3 hafta eklenir.
        await using var ctx2 = _db.NewContext();
        (await NewService(ctx2, later).TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(3);
        (await RuleSlotDatesAsync(created.Rule!.Id)).ShouldNotContain(raceDate);
    }

    [Fact]
    public async Task TopUpAsync_NonUniqueDbUpdateException_Propagates()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await CreateRuleAsync(WednesdayRule());

        // Kaydetmeden hemen önce öğretmen fiziksel silinir → FK ihlali (unique değil) → yutulmaz.
        var interceptor = new ConcurrentInsertInterceptor(async () =>
        {
            await using var other = _db.NewContext();
            await other.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON; DELETE FROM \"Teachers\" WHERE \"Id\" = {0}", TeacherId);
        });

        await using var ctx = _db.NewContext(interceptor);
        await Should.ThrowAsync<DbUpdateException>(() => NewService(ctx, FixedNow.AddDays(30)).TopUpAsync(TeacherId, TeacherUserId));
    }

    [Fact]
    public void IsUniqueViolation_RecognisesOnlyUniqueConstraintFailures()
    {
        var unique = new DbUpdateException("save failed",
            new Microsoft.Data.Sqlite.SqliteException("SQLite Error 19: 'UNIQUE constraint failed: TeacherAvailabilitySlots.TeacherId'.", 19));
        var other = new DbUpdateException("save failed",
            new Microsoft.Data.Sqlite.SqliteException("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19));
        var plain = new DbUpdateException("save failed", new InvalidOperationException("boom"));

        RecurringAvailabilityService.IsUniqueViolation(unique).ShouldBeTrue();
        RecurringAvailabilityService.IsUniqueViolation(other).ShouldBeFalse();
        RecurringAvailabilityService.IsUniqueViolation(plain).ShouldBeFalse();
    }

    [Fact]
    public async Task GetTeacherOpenSlotsAsync_StudentView_DoesNotExposeRuleId()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        // GetTeacherOpenSlotsAsync gerçek DateTime.UtcNow ile "gelecek" filtreler → kural gerçek bugünden.
        var realNow = DateTime.UtcNow;
        await using (var ctxCreate = _db.NewContext())
        {
            var created = await NewService(ctxCreate, realNow).CreateRuleAsync(TeacherUserId,
                WednesdayRule(effectiveFrom: DateOnly.FromDateTime(realNow).AddDays(1)));
            created.Success.ShouldBeTrue();
            created.GeneratedSlotIds.Count.ShouldBeGreaterThan(0);
        }

        await using var ctx = _db.NewContext();
        var result = await NewBookingService(ctx).GetTeacherOpenSlotsAsync(TeacherId, ExamApp.Api.Services.Tenancy.SchoolScope.For(StudentUserId, null), 0, 200);

        result.Success.ShouldBeTrue();
        result.Items.Count.ShouldBeGreaterThan(0);
        result.Items.ShouldAllBe(i => i.RecurringAvailabilityRuleId == null);
    }

    [Fact]
    public async Task GetMySlotsAsync_TriggersTopUpAndMarksRecurringSlots()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var created = await CreateRuleAsync(WednesdayRule());
        var singleSlotId = await SeedSlotAsync(TeacherId, new DateOnly(2026, 1, 9), new TimeOnly(9, 0), new TimeOnly(10, 0));

        await using var ctx = _db.NewContext();
        var result = await NewBookingService(ctx, FixedNow.AddDays(30)).GetMySlotsAsync(TeacherUserId, 0, 200);

        result.Success.ShouldBeTrue();
        var recurring = result.Items.Where(i => i.RecurringAvailabilityRuleId == created.Rule!.Id).ToList();
        recurring.Count.ShouldBe(17); // 13 + top-up 4
        recurring.ShouldContain(i => i.Date == new DateOnly(2026, 4, 29));
        result.Items.Single(i => i.Id == singleSlotId).RecurringAvailabilityRuleId.ShouldBeNull();
    }

    // ------ Listeleme ------

    [Fact]
    public async Task GetMyRulesAsync_ReturnsOnlyOwnActiveRules()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedTeacherAsync(OtherTeacherId, OtherTeacherUserId);
        var mine = await CreateRuleAsync(WednesdayRule());
        await CreateRuleAsync(WednesdayRule(), OtherTeacherUserId);

        var deleted = await CreateRuleAsync(new CreateRecurringAvailabilityRuleDto
        {
            DayOfWeek = DayOfWeek.Friday, StartTime = new(9, 0), EndTime = new(10, 0), EffectiveFrom = Today
        });
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).DeleteRuleAsync(TeacherUserId, deleted.Rule!.Id);
        }

        await using var ctxList = _db.NewContext();
        var result = await NewService(ctxList).GetMyRulesAsync(TeacherUserId, 0, 50);

        result.Success.ShouldBeTrue();
        result.Items.Select(r => r.Id).ShouldBe(new[] { mine.Rule!.Id });
        result.Items[0].TeacherId.ShouldBe(TeacherId);
    }

    // ------ Tüm seri silme ------

    [Fact]
    public async Task DeleteRuleAsync_DeletesUnbookedFutureSlots_PreservesBookedOnes_AndStopsTopUp()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedStudentAsync();
        var created = await CreateRuleAsync(WednesdayRule());
        var bookedSlotId = created.GeneratedSlotIds[2];
        await SeedBookingAsync(bookedSlotId);

        RecurringAvailabilityRuleDeleteResultDto result;
        await using (var ctx = _db.NewContext())
        {
            result = await NewService(ctx).DeleteRuleAsync(TeacherUserId, created.Rule!.Id);
        }

        result.Success.ShouldBeTrue();
        result.DeletedSlotIds.Count.ShouldBe(12);
        result.PreservedSlotIds.ShouldBe(new[] { bookedSlotId });
        result.PreservedBookedCount.ShouldBe(1);

        await using (var ctx = _db.NewContext())
        {
            var rule = await ctx.RecurringAvailabilityRules.IgnoreQueryFilters().SingleAsync(r => r.Id == created.Rule!.Id);
            rule.IsActive.ShouldBeFalse();
            rule.IsDeleted.ShouldBeTrue();
            rule.EffectiveUntil.ShouldBe(Today);

            var remaining = await ctx.TeacherAvailabilitySlots.Where(s => s.RecurringAvailabilityRuleId == rule.Id).ToListAsync();
            remaining.Select(s => s.Id).ShouldBe(new[] { bookedSlotId });

            // Silinen satırlar soft-delete: referans korunur.
            (await ctx.TeacherAvailabilitySlots.IgnoreQueryFilters().CountAsync(s => s.RecurringAvailabilityRuleId == rule.Id))
                .ShouldBe(13);
        }

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx, FixedNow.AddDays(30)).TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(0);
        }
    }

    [Fact]
    public async Task DeleteRuleAsync_NotYetStartedRule_KeepsEffectiveUntilNotBeforeEffectiveFrom()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var from = new DateOnly(2026, 1, 15);
        var created = await CreateRuleAsync(WednesdayRule(effectiveFrom: from));

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).DeleteRuleAsync(TeacherUserId, created.Rule!.Id)).Success.ShouldBeTrue();
        }

        await using var ctxCheck = _db.NewContext();
        var rule = await ctxCheck.RecurringAvailabilityRules.IgnoreQueryFilters().SingleAsync(r => r.Id == created.Rule!.Id);
        rule.EffectiveUntil.ShouldBe(from); // max(today, EffectiveFrom)
        rule.IsActive.ShouldBeFalse();
        (await RuleSlotDatesAsync(rule.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteRuleAsync_OtherTeachersRule_FailsWithForbidden()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        await SeedTeacherAsync(OtherTeacherId, OtherTeacherUserId);
        var created = await CreateRuleAsync(WednesdayRule());

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).DeleteRuleAsync(OtherTeacherUserId, created.Rule!.Id);

        result.Success.ShouldBeFalse();
        result.Forbidden.ShouldBeTrue();
        result.NotFound.ShouldBeFalse();
        (await RuleSlotDatesAsync(created.Rule.Id)).Count.ShouldBe(13);
    }

    [Fact]
    public async Task DeleteRuleAsync_UnknownRule_FailsWithNotFound()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).DeleteRuleAsync(TeacherUserId, 999);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteRuleAsync_AllowsRecreatingSameRuleAfterwards()
    {
        await SeedTeacherAsync(TeacherId, TeacherUserId);
        var first = await CreateRuleAsync(WednesdayRule());
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).DeleteRuleAsync(TeacherUserId, first.Rule!.Id);
        }

        // Filtreli unique index yalnız aktif+silinmemiş kuralı sayar → yeniden oluşturulabilir.
        var second = await CreateRuleAsync(WednesdayRule());

        second.Success.ShouldBeTrue();
        second.Rule!.Id.ShouldNotBe(first.Rule!.Id);
        (await RuleSlotDatesAsync(second.Rule.Id)).Count.ShouldBe(13);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>SaveChanges'ten hemen önce (sorgu bitti, INSERT henüz yok) bir kez "eşzamanlı yazıcı" çalıştırır.</summary>
    private sealed class ConcurrentInsertInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private readonly Func<Task> _concurrentWrite;

        public ConcurrentInsertInterceptor(Func<Task> concurrentWrite) => _concurrentWrite = concurrentWrite;

        public bool Fired { get; private set; }

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await _concurrentWrite();
            }

            return result;
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTime _fixedNow;

        public FakeTimeProvider(DateTime fixedNow) => _fixedNow = fixedNow;

        public override DateTimeOffset GetUtcNow() => new(_fixedNow);
    }
}
