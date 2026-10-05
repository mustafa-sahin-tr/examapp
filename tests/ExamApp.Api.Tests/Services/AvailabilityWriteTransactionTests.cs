using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Bookings;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Video;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #323 (security L2): müsaitlik yazan üç yol (tekil slot, tekrarlayan kural + üretim, top-up) çakışma kontrolü ile
/// INSERT'i öğretmen kilidi altında, execution strategy içinde açılmış tek transaction'da yapar. SQLite'ta advisory lock
/// no-op'tur; burada (1) transaction'ın strategy içinde açıldığı (retry'lı strateji guard'ı), (2) commit'te geçici hata sonrası
/// retry'ın satırları TAM BİR KEZ yazdığı ve (3) kilit yardımcısının transaction dışında reddedildiği doğrulanır. Gerçek
/// eşzamanlılık: <c>AvailabilitySlotIntegrityPostgresTests</c> (IntegrationTests).
/// </summary>
public class AvailabilityWriteTransactionTests : IDisposable
{
    private const int TeacherId = 10;
    private const int TeacherUserId = 100;

    /// <summary>Pazartesi 2026-06-15 12:00 UTC.</summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _clock = new(Now);

    public AvailabilityWriteTransactionTests()
    {
        using var ctx = _db.NewContext();
        ctx.Teachers.Add(new Teacher { Id = TeacherId, UserId = TeacherUserId, ApprovalStatus = TeacherApprovalStatus.Approved, Bio = "t" });
        ctx.SaveChanges();
    }

    private RecurringAvailabilityService NewRecurring(AppDbContext ctx)
        => new(ctx, _clock, NullLogger<RecurringAvailabilityService>.Instance);

    private BookingService NewBooking(AppDbContext ctx)
        => new(ctx, Substitute.For<IAuthApiClient>(), Substitute.For<IVideoSessionProvider>(), Options.Create(new VideoOptions()),
            _clock, NewRecurring(ctx), NullLogger<BookingService>.Instance, new ExamApp.Api.Services.Tenancy.SchoolAccessPolicy(ctx));

    private static CreateAvailabilitySlotDto Slot(DateOnly date, int startHour, int endHour) => new()
    {
        Date = date,
        StartTime = new TimeOnly(startHour, 0),
        EndTime = new TimeOnly(endHour, 0)
    };

    private static CreateRecurringAvailabilityRuleDto WednesdayRule(DateOnly? until = null) => new()
    {
        DayOfWeek = DayOfWeek.Wednesday,
        StartTime = new TimeOnly(14, 0),
        EndTime = new TimeOnly(15, 0),
        EffectiveFrom = Today,
        EffectiveUntil = until
    };

    private async Task<List<TeacherAvailabilitySlot>> SlotsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.TeacherAvailabilitySlots.AsNoTracking().OrderBy(s => s.Date).ToListAsync();
    }

    // ---------------- kilit yardımcısı ----------------

    [Fact]
    public async Task Lock_outside_a_transaction_is_rejected()
    {
        await using var ctx = _db.NewContext();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => ctx.Database.AcquireTeacherAvailabilityLockAsync(TeacherId));
        ex.Message.ShouldContain("transaction");
    }

    [Fact]
    public async Task Lock_inside_a_transaction_is_a_no_op_on_non_postgres_providers()
    {
        await using var ctx = _db.NewContext();
        await using var tx = await ctx.Database.BeginTransactionAsync();
        await ctx.Database.AcquireTeacherAvailabilityLockAsync(TeacherId);
        await tx.CommitAsync();
    }

    [Fact]
    public void Lock_class_is_distinct_from_other_advisory_lock_users()
        => TeacherAvailabilityLock.LockClass.ShouldNotBe(UserRegistrationLock.LockClass);

    // ---------------- transaction execution strategy içinde ----------------

    [Fact]
    public async Task CreateSlot_opens_its_transaction_inside_a_retrying_execution_strategy()
    {
        await using var ctx = _db.NewContextWithRetryingExecutionStrategy();
        var result = await NewBooking(ctx).CreateSlotAsync(TeacherUserId, Slot(Today.AddDays(2), 10, 11));

        result.Success.ShouldBeTrue(result.Message);
        (await SlotsAsync()).ShouldHaveSingleItem().Id.ShouldBe(result.ObjectId);
    }

    [Fact]
    public async Task CreateRule_and_TopUp_open_their_transactions_inside_a_retrying_execution_strategy()
    {
        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
            (await NewRecurring(ctx).CreateRuleAsync(TeacherUserId, WednesdayRule())).Success.ShouldBeTrue();

        _clock.Now = Now.AddDays(14); // ufuk kayar → top-up yeni haftalar üretir
        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
            (await NewRecurring(ctx).TopUpAsync(TeacherId, TeacherUserId)).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task CreateSlot_overlap_under_the_lock_returns_conflict_and_writes_nothing()
    {
        await using (var ctx = _db.NewContext())
            (await NewBooking(ctx).CreateSlotAsync(TeacherUserId, Slot(Today.AddDays(2), 10, 12))).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            var result = await NewBooking(ctx).CreateSlotAsync(TeacherUserId, Slot(Today.AddDays(2), 11, 13));
            result.Success.ShouldBeFalse();
            result.Conflict.ShouldBeTrue();
            ctx.ChangeTracker.Entries().ShouldBeEmpty();
        }

        (await SlotsAsync()).Count.ShouldBe(1);
    }

    // ---------------- commit'te geçici hata → retry tam bir kez yazar ----------------

    [Fact]
    public async Task CreateSlot_failed_commit_retry_writes_the_slot_exactly_once()
    {
        var interceptor = new FailFirstCommitInterceptor();
        AvailabilitySlotResultDto result;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            result = await NewBooking(ctx).CreateSlotAsync(TeacherUserId, Slot(Today.AddDays(2), 10, 11));

        interceptor.Failures.ShouldBe(1);
        result.Success.ShouldBeTrue(result.Message);
        var slot = (await SlotsAsync()).ShouldHaveSingleItem();
        slot.Id.ShouldBe(result.ObjectId);
        result.Slot!.Id.ShouldBe(slot.Id);
    }

    [Fact]
    public async Task CreateRule_failed_commit_retry_writes_rule_and_series_exactly_once()
    {
        var interceptor = new FailFirstCommitInterceptor();
        RecurringAvailabilityRuleResultDto result;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            result = await NewRecurring(ctx).CreateRuleAsync(TeacherUserId, WednesdayRule(until: Today.AddDays(20)));

        interceptor.Failures.ShouldBe(1);
        result.Success.ShouldBeTrue(result.Message);
        await using var check = _db.NewContext();
        var rule = (await check.RecurringAvailabilityRules.AsNoTracking().ToListAsync()).ShouldHaveSingleItem();
        rule.Id.ShouldBe(result.ObjectId);
        var slots = await SlotsAsync();
        slots.Count.ShouldBe(3); // 17, 24 Haziran ve 1 Temmuz Çarşambaları
        slots.ShouldAllBe(s => s.RecurringAvailabilityRuleId == rule.Id);
        result.GeneratedSlotIds.ShouldBe(slots.Select(s => s.Id), ignoreOrder: true);
    }

    [Fact]
    public async Task TopUp_failed_commit_retry_writes_new_occurrences_exactly_once()
    {
        await using (var ctx = _db.NewContext())
            (await NewRecurring(ctx).CreateRuleAsync(TeacherUserId, WednesdayRule())).Success.ShouldBeTrue();
        var before = (await SlotsAsync()).Count;

        _clock.Now = Now.AddDays(14);
        var interceptor = new FailFirstCommitInterceptor();
        int added;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            added = await NewRecurring(ctx).TopUpAsync(TeacherId, TeacherUserId);

        interceptor.Failures.ShouldBe(1);
        added.ShouldBe(2);
        var slots = await SlotsAsync();
        slots.Count.ShouldBe(before + 2);
        slots.Select(s => s.Date).Distinct().Count().ShouldBe(slots.Count); // aynı tarihe çift satır yok
    }

    [Fact]
    public async Task TopUp_with_nothing_to_add_does_not_open_a_transaction()
    {
        await using (var ctx = _db.NewContext())
            (await NewRecurring(ctx).CreateRuleAsync(TeacherUserId, WednesdayRule())).Success.ShouldBeTrue();

        // Ufuk kaymadı: ön plan boş → yazma yolu (ve kilit) hiç çalışmaz; commit'te patlayacak interceptor tetiklenmez.
        var interceptor = new FailFirstCommitInterceptor();
        await using var ctx2 = _db.NewContextWithTransientRetry(interceptor);
        (await NewRecurring(ctx2).TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(0);
        interceptor.Failures.ShouldBe(0);
    }

    // ---------------- review turu: kilit zaman aşımı sabiti ----------------

    [Fact]
    public void Lock_timeout_sql_matches_the_timeout_constant()
    {
        TeacherAvailabilityLock.SetLockTimeoutSql.ShouldBe($"SET LOCAL lock_timeout = '{TeacherAvailabilityLock.LockTimeoutSeconds}s'");
        TeacherAvailabilityLock.LockTimeout.ShouldBe(TimeSpan.FromSeconds(5));
    }

    // ---------------- review turu: CreateSlot doğrulama + hata sınıflandırma ----------------

    [Theory]
    [InlineData(0, 30)]
    [InlineData(30, 0)]
    public async Task CreateSlot_with_seconds_is_rejected_as_a_validation_error(int startSecond, int endSecond)
    {
        await using var ctx = _db.NewContext();
        var result = await NewBooking(ctx).CreateSlotAsync(TeacherUserId, new CreateAvailabilitySlotDto
        {
            Date = Today.AddDays(2),
            StartTime = new TimeOnly(10, 0, startSecond),
            EndTime = new TimeOnly(11, 0, endSecond)
        });

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeFalse();   // 400 (MapFailure: bayraksız hata)
        result.NotFound.ShouldBeFalse();
        result.Forbidden.ShouldBeFalse();
        result.Message.ShouldBe(FallbackMessageLocalizer.Instance["booking.slot.invalidPrecision"].Value);
        result.Message.ShouldNotBe("booking.slot.invalidPrecision");
        (await SlotsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateSlot_non_unique_db_failure_propagates_instead_of_being_reported_as_a_duplicate()
    {
        // Kaydetmeden hemen önce öğretmen fiziksel silinir → FK ihlali (unique değil) → 409 "duplicate" diye yutulmaz.
        var interceptor = new BeforeSaveInterceptor(async owner =>
        {
            await using var other = _db.NewContext();
            await other.Database.UseTransactionAsync(owner.Database.CurrentTransaction!.GetDbTransaction());
            await other.Database.ExecuteSqlRawAsync("DELETE FROM \"Teachers\" WHERE \"Id\" = {0}", TeacherId);
        });

        await using var ctx = _db.NewContext(interceptor);
        await Should.ThrowAsync<DbUpdateException>(() => NewBooking(ctx).CreateSlotAsync(TeacherUserId, Slot(Today.AddDays(2), 10, 11)));
        interceptor.Fired.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateSlot_lost_commit_acknowledgement_retry_reports_success_for_its_own_row()
    {
        // COMMIT veritabanına ulaştı ama onay kayboldu → strateji yeniden denedi; yeni deneme kendi satırını kesişen slot
        // olarak görür. Code review Uyarı-2: aynı aralık + aynı yazan + aynı CreatedAt → 409 değil başarı.
        var interceptor = new FailAfterFirstCommitInterceptor();
        AvailabilitySlotResultDto result;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            result = await NewBooking(ctx).CreateSlotAsync(TeacherUserId, Slot(Today.AddDays(2), 10, 11));

        interceptor.Failures.ShouldBe(1);
        result.Success.ShouldBeTrue(result.Message);
        result.Conflict.ShouldBeFalse();
        var slot = (await SlotsAsync()).ShouldHaveSingleItem();
        result.ObjectId.ShouldBe(slot.Id);
        result.Slot!.Id.ShouldBe(slot.Id);
    }

    [Fact]
    public async Task CreateSlot_retry_still_conflicts_with_a_different_writers_identical_slot()
    {
        // Başka bir istek (farklı CreatedAt) aynı aralığı açmışsa retry'daki "kendi satırım" eşleşmesi tutmamalı.
        await using (var ctx = _db.NewContext())
            (await NewBooking(ctx).CreateSlotAsync(TeacherUserId, Slot(Today.AddDays(2), 10, 11))).Success.ShouldBeTrue();

        _clock.Now = Now.AddMinutes(1);
        var interceptor = new FailFirstCommitInterceptor();
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
        {
            var result = await NewBooking(ctx).CreateSlotAsync(TeacherUserId, Slot(Today.AddDays(2), 10, 11));
            result.Success.ShouldBeFalse();
            result.Conflict.ShouldBeTrue();
        }

        interceptor.Failures.ShouldBe(0); // çakışma: commit'e hiç gelinmedi
        (await SlotsAsync()).Count.ShouldBe(1);
    }

    // ---------------- review turu: seri silme kilit/transaction ----------------

    [Fact]
    public async Task DeleteRule_runs_inside_a_retrying_execution_strategy_and_a_failed_commit_retry_deletes_once()
    {
        int ruleId;
        await using (var ctx = _db.NewContext())
            ruleId = (await NewRecurring(ctx).CreateRuleAsync(TeacherUserId, WednesdayRule(until: Today.AddDays(20)))).ObjectId;

        var interceptor = new FailFirstCommitInterceptor();
        RecurringAvailabilityRuleDeleteResultDto result;
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            result = await NewRecurring(ctx).DeleteRuleAsync(TeacherUserId, ruleId);

        interceptor.Failures.ShouldBe(1);
        result.Success.ShouldBeTrue(result.Message);
        result.DeletedSlotIds.Count.ShouldBe(3); // listeler denemeler arasında birikmez
        (await SlotsAsync()).ShouldBeEmpty();
        await using var check = _db.NewContext();
        (await check.RecurringAvailabilityRules.IgnoreQueryFilters().SingleAsync()).IsDeleted.ShouldBeTrue();

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
            (await NewRecurring(ctx).DeleteRuleAsync(TeacherUserId, ruleId)).NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task TopUp_rereads_rules_inside_its_transaction_and_skips_a_rule_stopped_in_between()
    {
        await using (var ctx = _db.NewContext())
            (await NewRecurring(ctx).CreateRuleAsync(TeacherUserId, WednesdayRule())).Success.ShouldBeTrue();
        var before = (await SlotsAsync()).Count;

        // Ön okuma (kilitsiz) kuralı aktif gördü; transaction başladığında (kilit alınırken) kural seri silmeyle durdurulur.
        _clock.Now = Now.AddDays(14);
        var interceptor = new OnTransactionStartedInterceptor(async (transaction) =>
        {
            await using var other = _db.NewContext();
            await other.Database.UseTransactionAsync(transaction);
            await other.Database.ExecuteSqlRawAsync("UPDATE \"RecurringAvailabilityRules\" SET \"IsActive\" = 0, \"IsDeleted\" = 1");
        });

        await using (var ctx = _db.NewContext(interceptor))
            (await NewRecurring(ctx).TopUpAsync(TeacherId, TeacherUserId)).ShouldBe(0);

        interceptor.Fired.ShouldBeTrue();
        (await SlotsAsync()).Count.ShouldBe(before);
    }

    /// <summary>SaveChanges'ten hemen önce bir kez "eşzamanlı yazıcı" çalıştırır (kaydeden context'i alır).</summary>
    private sealed class BeforeSaveInterceptor(Func<AppDbContext, Task> write) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await write((AppDbContext)eventData.Context!);
            }

            return result;
        }
    }

    /// <summary>İlk transaction açıldığında (kilit alınmadan hemen önce) bir kez yazıcı çalıştırır.</summary>
    private sealed class OnTransactionStartedInterceptor(Func<System.Data.Common.DbTransaction, Task> write)
        : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<System.Data.Common.DbTransaction> TransactionStartedAsync(
            System.Data.Common.DbConnection connection, Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData,
            System.Data.Common.DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await write(result);
            }

            return result;
        }
    }

    /// <summary>İlk COMMIT veritabanına ULAŞTIKTAN sonra geçici hata fırlatır (onay kaybı) — strateji yeniden dener.</summary>
    private sealed class FailAfterFirstCommitInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.DbTransactionInterceptor
    {
        public int Failures { get; private set; }

        public override Task TransactionCommittedAsync(System.Data.Common.DbTransaction transaction,
            Microsoft.EntityFrameworkCore.Diagnostics.TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Failures == 0)
            {
                Failures++;
                throw new TransientTestException("simulated lost commit acknowledgement");
            }

            return Task.CompletedTask;
        }
    }

    public void Dispose() => _db.Dispose();
}
