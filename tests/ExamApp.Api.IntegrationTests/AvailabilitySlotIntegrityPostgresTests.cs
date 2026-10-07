using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos.Bookings;
using ExamApp.Api.Services.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #323 — müsaitlik slotu bütünlüğü gerçek PostgreSQL'de:
/// (O1) <c>TeacherAvailabilitySlots</c> / <c>RecurringAvailabilityRules</c> CHECK constraint'i doğrudan SQL'i de reddeder
/// (sıfır süre, 4 saati aşan süre — gün aşımı dahil); migration ihlal eden mevcut satırda açık mesajla durur.
/// (L2) Slot yazan yollar öğretmen bazlı <c>pg_advisory_xact_lock</c> altında: paralel iki kesişen slot isteğinden yalnız
/// biri başarılı olur (birim testler SQLite'ta tek bağlantıyla koştuğu için yarışı yalnız burada gerçekten üretebiliriz).
/// </summary>
public class AvailabilitySlotIntegrityPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string SlotCheck = "CK_TeacherAvailabilitySlots_Duration";
    private const string RuleCheck = "CK_RecurringAvailabilityRules_Duration";
    private const int Rounds = 12;

    private static readonly DateOnly Future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(7);

    private Task<int> SeedTeacherAsync(int userId) => WithDbAsync(async db =>
    {
        var teacher = new Teacher
        {
            UserId = userId,
            ApprovalStatus = TeacherApprovalStatus.Approved,
            IsIndependentTutor = true, // issue #418: müsaitlik bağımsız öğretmen özelliği
            AccountApprovedAt = DateTime.UtcNow,
            Bio = "t"
        };
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        return teacher.Id;
    });

    private async Task<PostgresException?> TryInsertSlotAsync(int teacherId, string start, string end)
    {
        try
        {
            await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "TeacherAvailabilitySlots" ("TeacherId", "Date", "StartTime", "EndTime", "CreatedAt", "CreateTime", "IsDeleted")
                VALUES ({0}, {1}, CAST({2} AS time), CAST({3} AS time), now(), now(), FALSE)
                """, teacherId, Future, start, end));
            return null;
        }
        catch (PostgresException ex)
        {
            return ex;
        }
    }

    private async Task<PostgresException?> TryInsertRuleAsync(int teacherId, string start, string end)
    {
        try
        {
            await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "RecurringAvailabilityRules" ("TeacherId", "DayOfWeek", "StartTime", "EndTime", "EffectiveFrom", "IsActive", "CreateTime", "IsDeleted")
                VALUES ({0}, 1, CAST({1} AS time), CAST({2} AS time), {3}, TRUE, now(), FALSE)
                """, teacherId, start, end, Future));
            return null;
        }
        catch (PostgresException ex)
        {
            return ex;
        }
    }

    /// <summary>(başlangıç, bitiş, kabul?) — servis kuralı: bitiş &lt; başlangıç ise ertesi gün; süre 0 &lt; d &lt;= 4 saat.</summary>
    public static TheoryData<string, string, bool> DurationCases() => new()
    {
        { "10:00", "10:00", false }, // sıfır süre
        { "00:00", "00:00", false }, // sıfır süre (gece yarısı)
        { "10:00", "14:00", true },  // tam 4 saat
        { "10:00", "14:01", false }, // 4 saat 1 dk
        { "09:00", "18:00", false }, // 9 saat
        { "23:30", "00:30", true },  // gün aşan 1 saat
        { "22:00", "02:00", true },  // gün aşan tam 4 saat
        { "22:00", "02:01", false }, // gün aşan 4 saat 1 dk
        { "10:00", "09:00", false }, // gün aşan 23 saat (eski "bitiş < başlangıç" hatalı veri)
        { "23:59", "00:00", true },  // gün aşan 1 dk
    };

    [Theory]
    [MemberData(nameof(DurationCases))]
    public async Task Slot_check_constraint_rejects_direct_sql_outside_the_service_duration_rule(string start, string end, bool accepted)
    {
        var teacherId = await SeedTeacherAsync(32_301);

        var error = await TryInsertSlotAsync(teacherId, start, end);

        if (accepted)
        {
            error.ShouldBeNull();
        }
        else
        {
            error.ShouldNotBeNull();
            error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
            error.ConstraintName.ShouldBe(SlotCheck);
        }

        // Aynı kural servisin bellekteki hesabıyla birebir (SlotTimeRange tek yorum noktası).
        var (s, e) = (TimeOnly.Parse(start), TimeOnly.Parse(end));
        var serviceAccepts = !SlotTimeRange.IsZeroLength(s, e) && SlotTimeRange.DurationOf(s, e) <= TimeSpan.FromHours(4);
        serviceAccepts.ShouldBe(accepted);
    }

    [Theory]
    [MemberData(nameof(DurationCases))]
    public async Task Rule_check_constraint_rejects_direct_sql_outside_the_service_duration_rule(string start, string end, bool accepted)
    {
        var teacherId = await SeedTeacherAsync(32_302);

        var error = await TryInsertRuleAsync(teacherId, start, end);

        if (accepted)
        {
            error.ShouldBeNull();
        }
        else
        {
            error.ShouldNotBeNull();
            error.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
            error.ConstraintName.ShouldBe(RuleCheck);
        }
    }

    [Fact]
    public async Task Check_constraint_also_rejects_updates_that_stretch_an_existing_slot()
    {
        var teacherId = await SeedTeacherAsync(32_303);
        (await TryInsertSlotAsync(teacherId, "10:00", "11:00")).ShouldBeNull();

        var ex = await Should.ThrowAsync<PostgresException>(() => WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            """UPDATE "TeacherAvailabilitySlots" SET "EndTime" = CAST('15:00' AS time) WHERE "TeacherId" = {0}""", teacherId)));
        ex.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    // ------------------------------------------------------------------
    // Eşzamanlı çakışma (security L2)
    // ------------------------------------------------------------------

    private async Task<AvailabilitySlotResultDto> CreateSlotAsync(int teacherUserId, DateOnly date, TimeOnly start, TimeOnly end, SemaphoreSlim gate)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IBookingService>();
        await gate.WaitAsync();
        return await service.CreateSlotAsync(teacherUserId, new CreateAvailabilitySlotDto { Date = date, StartTime = start, EndTime = end });
    }

    private Task<List<SlotTimeRange>> LiveRangesAsync(int teacherId) => WithDbAsync(async db =>
        (await db.TeacherAvailabilitySlots.AsNoTracking()
            .Where(s => s.TeacherId == teacherId)
            .Select(s => new { s.Date, s.StartTime, s.EndTime })
            .ToListAsync())
        .Select(s => SlotTimeRange.From(s.Date, s.StartTime, s.EndTime))
        .ToList());

    private static void ShouldHaveNoOverlaps(List<SlotTimeRange> ranges, string because)
    {
        for (var i = 0; i < ranges.Count; i++)
            for (var j = i + 1; j < ranges.Count; j++)
                ranges[i].Overlaps(ranges[j]).ShouldBeFalse($"{because}: {ranges[i]} / {ranges[j]}");
    }

    [Fact]
    public async Task Parallel_overlapping_slot_requests_for_the_same_teacher_create_exactly_one_slot()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var userId = 32_400 + round;
            var teacherId = await SeedTeacherAsync(userId);
            var date = Future.AddDays(round % 5);
            using var gate = new SemaphoreSlim(0, 2);

            // Kesişen ama birebir aynı olmayan aralıklar: unique index (TeacherId, Date, Start, End) bunları yakalamaz.
            var a = Task.Run(() => CreateSlotAsync(userId, date, new TimeOnly(10, 0), new TimeOnly(11, 0), gate));
            var b = Task.Run(() => CreateSlotAsync(userId, date, new TimeOnly(10, 30), new TimeOnly(11, 30), gate));
            gate.Release(2);
            var results = await Task.WhenAll(a, b);

            results.Count(r => r.Success).ShouldBe(1, $"round {round}: tam olarak bir slot açılmalı");
            results.Single(r => !r.Success).Conflict.ShouldBeTrue($"round {round}: kaybeden 409 almalı");
            (await LiveRangesAsync(teacherId)).Count.ShouldBe(1, $"round {round}");
        }
    }

    [Fact]
    public async Task Parallel_crossing_midnight_slot_and_next_day_slot_create_exactly_one()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var userId = 32_500 + round;
            var teacherId = await SeedTeacherAsync(userId);
            var date = Future.AddDays(round % 5);
            using var gate = new SemaphoreSlim(0, 2);

            // issue #300 örneği: d 23:30 → d+1 00:30 ile d+1 00:00–00:45 kesişir (farklı Date değerleri).
            var a = Task.Run(() => CreateSlotAsync(userId, date, new TimeOnly(23, 30), new TimeOnly(0, 30), gate));
            var b = Task.Run(() => CreateSlotAsync(userId, date.AddDays(1), new TimeOnly(0, 0), new TimeOnly(0, 45), gate));
            gate.Release(2);
            var results = await Task.WhenAll(a, b);

            results.Count(r => r.Success).ShouldBe(1, $"round {round}");
            results.Single(r => !r.Success).Conflict.ShouldBeTrue($"round {round}");
            (await LiveRangesAsync(teacherId)).Count.ShouldBe(1, $"round {round}");
        }
    }

    [Fact]
    public async Task Parallel_single_slot_and_recurring_rule_never_leave_overlapping_slots()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var userId = 32_600 + round;
            var teacherId = await SeedTeacherAsync(userId);
            var date = Future.AddDays(round % 7);
            using var gate = new SemaphoreSlim(0, 2);

            var slot = Task.Run(() => CreateSlotAsync(userId, date, new TimeOnly(14, 30), new TimeOnly(15, 30), gate));
            var rule = Task.Run(async () =>
            {
                using var scope = Factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IRecurringAvailabilityService>();
                await gate.WaitAsync();
                return await service.CreateRuleAsync(userId, new CreateRecurringAvailabilityRuleDto
                {
                    DayOfWeek = date.DayOfWeek,
                    StartTime = new TimeOnly(14, 0),
                    EndTime = new TimeOnly(15, 0),
                    EffectiveFrom = date,
                    EffectiveUntil = date.AddDays(21)
                });
            });
            gate.Release(2);
            await Task.WhenAll(slot, rule);
            var slotResult = await slot;
            var ruleResult = await rule;

            // Hangisi önce kilidi alırsa: tekil slot önceyse kural o haftayı atlar; kural önceyse tekil slot 409 alır.
            ruleResult.Success.ShouldBeTrue($"round {round}");
            if (slotResult.Success)
                ruleResult.SkippedDates.ShouldContain(date, $"round {round}: kural kesişen haftayı atlamalı");
            else
            {
                slotResult.Conflict.ShouldBeTrue($"round {round}");
                ruleResult.SkippedDates.ShouldNotContain(date, $"round {round}");
            }

            ShouldHaveNoOverlaps(await LiveRangesAsync(teacherId), $"round {round}");
        }
    }

    [Fact]
    public async Task Second_writer_waits_for_the_teacher_lock_and_then_sees_the_first_writers_slot()
    {
        const int userId = 32_700;
        var teacherId = await SeedTeacherAsync(userId);

        // Birinci yazıcı: kilidi alır, kesişen satırı yazar ama henüz commit etmez.
        using var holderScope = Factory.Services.CreateScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.AcquireTeacherAvailabilityLockAsync(teacherId);
        await holder.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "TeacherAvailabilitySlots" ("TeacherId", "Date", "StartTime", "EndTime", "CreatedAt", "CreateTime", "IsDeleted")
            VALUES ({0}, {1}, CAST('10:00' AS time), CAST('11:00' AS time), now(), now(), FALSE)
            """, teacherId, Future);

        using var gate = new SemaphoreSlim(0, 1);
        var second = Task.Run(() => CreateSlotAsync(userId, Future, new TimeOnly(10, 30), new TimeOnly(11, 30), gate));
        gate.Release();

        // Kilit tutuldukça ikinci istek bekler (kilitsiz eski kod burada commit edilmemiş satırı görmeden INSERT ederdi).
        (await Task.WhenAny(second, Task.Delay(TimeSpan.FromMilliseconds(750)))).ShouldNotBe(second);

        await tx.CommitAsync();
        var result = await second.WaitAsync(TimeSpan.FromSeconds(30));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        (await LiveRangesAsync(teacherId)).Count.ShouldBe(1);
    }

    // ------------------------------------------------------------------
    // Review turu: seri silme / top-up kilidi, kilit zaman aşımı, commit edilmiş kesişen slot
    // ------------------------------------------------------------------

    /// <summary>Gelecek tarihten başlayan, 3 haftalık, henüz occurrence'ı üretilmemiş aktif kural (doğrudan SQL).</summary>
    private Task<int> SeedRuleWithoutSlotsAsync(int teacherId, TimeOnly start, TimeOnly end) => WithDbAsync(async db =>
    {
        var rule = new RecurringAvailabilityRule
        {
            TeacherId = teacherId,
            DayOfWeek = Future.DayOfWeek,
            StartTime = start,
            EndTime = end,
            EffectiveFrom = Future,
            EffectiveUntil = Future.AddDays(14),
            IsActive = true
        };
        db.RecurringAvailabilityRules.Add(rule);
        await db.SaveChangesAsync();
        return rule.Id;
    });

    private async Task<(IServiceScope Scope, AppDbContext Db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction Tx)> HoldTeacherLockAsync(int teacherId)
    {
        var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tx = await db.Database.BeginTransactionAsync();
        await db.Database.AcquireTeacherAvailabilityLockAsync(teacherId);
        return (scope, db, tx);
    }

    private static async Task ShouldStillBeWaitingAsync(Task task)
        => (await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(750)))).ShouldNotBe(task);

    [Fact]
    public async Task TopUp_racing_a_series_delete_does_not_generate_slots_for_the_deleted_rule()
    {
        const int userId = 32_800;
        var teacherId = await SeedTeacherAsync(userId);
        var ruleId = await SeedRuleWithoutSlotsAsync(teacherId, new TimeOnly(14, 0), new TimeOnly(15, 0));

        // Seri silme kilidi tutuyor ve kuralı durdurdu, henüz commit etmedi.
        var (scope, holder, tx) = await HoldTeacherLockAsync(teacherId);
        using (scope)
        await using (tx)
        {
            await holder.Database.ExecuteSqlRawAsync(
                """UPDATE "RecurringAvailabilityRules" SET "IsActive" = FALSE, "IsDeleted" = TRUE WHERE "Id" = {0}""", ruleId);

            // Top-up'ın kilitsiz ön okuması kuralı hâlâ aktif görür (commit yok) → kilidi bekler.
            var topUp = Task.Run(async () =>
            {
                using var s = Factory.Services.CreateScope();
                return await s.ServiceProvider.GetRequiredService<IRecurringAvailabilityService>().TopUpAsync(teacherId, userId);
            });
            await ShouldStillBeWaitingAsync(topUp);

            await tx.CommitAsync();
            (await topUp.WaitAsync(TimeSpan.FromSeconds(30))).ShouldBe(0, "kilit altında yeniden okunan kural artık silinmiş");
        }

        (await LiveRangesAsync(teacherId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Series_delete_waits_for_the_teacher_lock()
    {
        const int userId = 32_801;
        var teacherId = await SeedTeacherAsync(userId);
        var ruleId = await SeedRuleWithoutSlotsAsync(teacherId, new TimeOnly(9, 0), new TimeOnly(10, 0));

        var (scope, _, tx) = await HoldTeacherLockAsync(teacherId);
        using (scope)
        await using (tx)
        {
            var delete = Task.Run(async () =>
            {
                using var s = Factory.Services.CreateScope();
                return await s.ServiceProvider.GetRequiredService<IRecurringAvailabilityService>().DeleteRuleAsync(userId, ruleId);
            });
            await ShouldStillBeWaitingAsync(delete);

            await tx.CommitAsync();
            (await delete.WaitAsync(TimeSpan.FromSeconds(30))).Success.ShouldBeTrue();
        }

        await WithDbAsync(async db =>
            (await db.RecurringAvailabilityRules.IgnoreQueryFilters().SingleAsync(r => r.Id == ruleId)).IsDeleted.ShouldBeTrue());
    }

    [Fact]
    public async Task Lock_timeout_returns_a_busy_conflict_without_execution_strategy_retries()
    {
        const int userId = 32_802;
        var teacherId = await SeedTeacherAsync(userId);

        var (scope, _, tx) = await HoldTeacherLockAsync(teacherId);
        using (scope)
        await using (tx)
        {
            using var gate = new SemaphoreSlim(0, 1);
            var started = System.Diagnostics.Stopwatch.StartNew();
            var create = Task.Run(() => CreateSlotAsync(userId, Future, new TimeOnly(10, 0), new TimeOnly(11, 0), gate));
            gate.Release();
            var result = await create.WaitAsync(TimeSpan.FromSeconds(60));
            started.Stop();

            result.Success.ShouldBeFalse();
            result.Conflict.ShouldBeTrue();
            result.Message.ShouldNotBe("booking.slot.busy"); // lokalize metin
            // Tek bir lock_timeout (5 sn): 55P03 execution strategy'nin yeniden denemesine girmez.
            started.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(4));
            started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(9));
        }

        (await LiveRangesAsync(teacherId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task TopUp_skips_the_week_of_an_overlapping_slot_committed_on_a_separate_connection()
    {
        const int userId = 32_803;
        var teacherId = await SeedTeacherAsync(userId);
        await SeedRuleWithoutSlotsAsync(teacherId, new TimeOnly(14, 0), new TimeOnly(15, 0));
        var clashDate = Future.AddDays(7);

        // Ayrı bağlantı + ayrı commit (EF/DI dışı): ikinci occurrence'la kesişen tekil slot.
        await using (var conn = new NpgsqlConnection(Factory.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO "TeacherAvailabilitySlots" ("TeacherId", "Date", "StartTime", "EndTime", "CreatedAt", "CreateTime", "IsDeleted")
                VALUES (@t, @d, TIME '14:30', TIME '15:30', now(), now(), FALSE)
                """, conn);
            cmd.Parameters.AddWithValue("t", teacherId);
            cmd.Parameters.AddWithValue("d", clashDate);
            await cmd.ExecuteNonQueryAsync();
        }

        int added;
        using (var s = Factory.Services.CreateScope())
            added = await s.ServiceProvider.GetRequiredService<IRecurringAvailabilityService>().TopUpAsync(teacherId, userId);

        added.ShouldBe(2); // 3 occurrence'tan kesişen hafta atlandı
        var ruleDates = await WithDbAsync(db => db.TeacherAvailabilitySlots.AsNoTracking()
            .Where(x => x.TeacherId == teacherId && x.RecurringAvailabilityRuleId != null)
            .Select(x => x.Date).ToListAsync());
        ruleDates.ShouldBe(new[] { Future, Future.AddDays(14) }, ignoreOrder: true);
        ShouldHaveNoOverlaps(await LiveRangesAsync(teacherId), "top-up sonrası");
    }

    // ------------------------------------------------------------------
    // Migration ön kontrolü
    // ------------------------------------------------------------------

    private const string PreviousMigration = "20260930183625_AddWorksheetCommentPinSourceAndClearCrossSchoolOwnerPins";
    private const string TargetMigration = "20261005124021_AddAvailabilitySlotDurationCheckConstraints";

    [Fact]
    public async Task Migration_stops_with_a_clear_error_when_existing_rows_violate_the_rule_and_does_not_touch_them()
    {
        var builder = new NpgsqlConnectionStringBuilder(Factory.ConnectionString);
        var tempDb = $"exam_test_migration_{Guid.NewGuid().ToString("N")[..8]}";
        builder.Database = "postgres";
        var adminConnStr = builder.ToString();
        await using (var conn = new NpgsqlConnection(adminConnStr))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{tempDb}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        builder.Database = tempDb;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ToString()).Options;
        try
        {
            await using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Teachers" ("Id", "UserId", "IsIndependentTutor", "IsDeleted", "CreateTime") VALUES (1, 1, TRUE, FALSE, now());
                    INSERT INTO "TeacherAvailabilitySlots" ("Id", "TeacherId", "Date", "StartTime", "EndTime", "CreatedAt", "CreateTime", "IsDeleted") VALUES
                        (1, 1, DATE '2026-11-02', TIME '10:00', TIME '11:00', now(), now(), FALSE),  -- geçerli
                        (2, 1, DATE '2026-11-03', TIME '09:00', TIME '09:00', now(), now(), TRUE),   -- sıfır süre (silinmiş satır da sayılır)
                        (3, 1, DATE '2026-11-04', TIME '20:00', TIME '01:00', now(), now(), FALSE);  -- gün aşan 5 saat
                    INSERT INTO "RecurringAvailabilityRules" ("Id", "TeacherId", "DayOfWeek", "StartTime", "EndTime", "EffectiveFrom", "IsActive", "CreateTime", "IsDeleted") VALUES
                        (1, 1, 1, TIME '08:00', TIME '13:00', DATE '2026-11-02', TRUE, now(), FALSE);  -- 5 saat
                    """);
            }

            await using (var ctx = new AppDbContext(options))
            {
                var ex = await Should.ThrowAsync<PostgresException>(() => ctx.GetService<IMigrator>().MigrateAsync(TargetMigration));
                ex.SqlState.ShouldBe(PostgresErrorCodes.RaiseException);
                ex.MessageText.ShouldContain("issue #323");
                ex.MessageText.ShouldContain("2 TeacherAvailabilitySlots");
                ex.MessageText.ShouldContain("1 RecurringAvailabilityRules");
            }

            await using (var ctx = new AppDbContext(options))
            {
                // Veri sessizce silinmedi/değiştirilmedi; migration uygulanmış sayılmadı.
                (await ctx.TeacherAvailabilitySlots.IgnoreQueryFilters().CountAsync()).ShouldBe(3);
                (await ctx.RecurringAvailabilityRules.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
                (await ctx.Database.GetAppliedMigrationsAsync()).ShouldNotContain(TargetMigration);

                // Elle düzeltme sonrası migration uygulanır ve kısıt devrededir.
                await ctx.Database.ExecuteSqlRawAsync("""
                    DELETE FROM "TeacherAvailabilitySlots" WHERE "Id" IN (2, 3);
                    UPDATE "RecurringAvailabilityRules" SET "EndTime" = TIME '12:00' WHERE "Id" = 1;
                    """);
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
                (await ctx.Database.GetAppliedMigrationsAsync()).ShouldContain(TargetMigration);

                var violation = await Should.ThrowAsync<PostgresException>(() => ctx.Database.ExecuteSqlRawAsync(
                    """UPDATE "TeacherAvailabilitySlots" SET "EndTime" = "StartTime" WHERE "Id" = 1"""));
                violation.ConstraintName.ShouldBe(SlotCheck);

                // Down: kısıtlar kalkar.
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                (await ctx.Database.SqlQueryRaw<int>(
                    """SELECT count(*)::int AS "Value" FROM pg_constraint WHERE conname IN ('CK_TeacherAvailabilitySlots_Duration', 'CK_RecurringAvailabilityRules_Duration')""")
                    .SingleAsync()).ShouldBe(0);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var conn = new NpgsqlConnection(adminConnStr);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{tempDb}\" WITH (FORCE)", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
