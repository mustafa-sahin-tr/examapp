using ExamApp.Api.Data;
using ExamApp.Api.Services.StudentPoints;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #422: <c>StudentPointsChangedEvent</c> mutlak toplam taşır; senkron, uygulanan her daha yeni değerin farkını event
/// anının YEREL (Europe/Istanbul) gününe <c>StudentDailyXps</c>'e yazar. İlk senkron (taban çizgisi) deftere yazılmaz; eski /
/// tekrar teslim edilen event defteri değiştirmez; sıfırlanmış (soft-delete) satırdan sonra fark 0'dan hesaplanır.
/// </summary>
public class StudentDailyXpLedgerTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    /// <param name="createdAt">Öğrenci kaydının açılış anı (varsayılan: event'lerden çok önce → ilk senkron taban çizgisi).</param>
    private async Task<int> SeedStudentAsync(int userId = 42, DateTime? createdAt = null)
    {
        await using var ctx = _db.NewContext();
        var s = new Student { UserId = userId, StudentNumber = $"S{userId}" };
        ctx.Students.Add(s);
        await ctx.SaveChangesAsync();
        // Audit hook CreateTime'ı "şimdi" yazar; testin istediği ana sabitle.
        var created = createdAt ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await ctx.Students.Where(x => x.Id == s.Id).ExecuteUpdateAsync(set => set.SetProperty(x => x.CreateTime, created));
        return s.Id;
    }

    private async Task<StudentPointsSyncResult> ApplyAsync(int points, DateTime version, int userId = 42)
    {
        await using var ctx = _db.NewContext();
        return await new StudentPointsSyncService(ctx, NullLogger<StudentPointsSyncService>.Instance)
            .ApplyAsync(new StudentPointsChangedEvent { UserId = userId, TotalPoints = points, UpdatedAtUtc = version });
    }

    private async Task<Dictionary<DateOnly, int>> LedgerAsync(int studentId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.StudentDailyXps.Where(d => d.StudentId == studentId).ToDictionaryAsync(d => d.Day, d => d.Xp);
    }

    private static DateTime Utc(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public async Task First_sync_is_a_baseline_and_later_differences_go_to_the_local_day()
    {
        var studentId = await SeedStudentAsync();

        (await ApplyAsync(1000, Utc(9, 5, 8))).ShouldBe(StudentPointsSyncResult.Applied); // geçmiş toplam → defter yok
        (await LedgerAsync(studentId)).ShouldBeEmpty();

        await ApplyAsync(1010, Utc(9, 5, 9));
        await ApplyAsync(1025, Utc(9, 5, 10));
        // 21:30Z = Istanbul 00:30 ertesi gün → 6 Eylül'e (UTC günü 5 Eylül olsa da).
        await ApplyAsync(1030, Utc(9, 5, 21, 30));

        (await LedgerAsync(studentId)).ShouldBe(new Dictionary<DateOnly, int>
        {
            [new DateOnly(2026, 9, 5)] = 25,
            [new DateOnly(2026, 9, 6)] = 5
        });
    }

    [Fact]
    public async Task Redelivered_or_older_events_do_not_touch_the_ledger_and_skipped_differences_fold_into_the_next()
    {
        var studentId = await SeedStudentAsync();
        await ApplyAsync(100, Utc(9, 6, 8));
        await ApplyAsync(130, Utc(9, 6, 10));

        (await ApplyAsync(130, Utc(9, 6, 10))).ShouldBe(StudentPointsSyncResult.Stale); // tekrar teslim
        (await ApplyAsync(120, Utc(9, 6, 9))).ShouldBe(StudentPointsSyncResult.Stale);  // sırasız eski
        (await LedgerAsync(studentId)).ShouldBe(new Dictionary<DateOnly, int> { [new DateOnly(2026, 9, 6)] = 30 });

        // Kaybolan ara event (140) yok: 130 → 150 farkı bir sonraki uygulanan event'in gününe.
        await ApplyAsync(150, Utc(9, 7, 9));
        (await LedgerAsync(studentId)).ShouldBe(new Dictionary<DateOnly, int>
        {
            [new DateOnly(2026, 9, 6)] = 30,
            [new DateOnly(2026, 9, 7)] = 20
        });
    }

    [Fact]
    public async Task After_a_reset_the_difference_starts_from_zero()
    {
        var studentId = await SeedStudentAsync();
        await ApplyAsync(500, Utc(9, 6, 8));
        await ApplyAsync(520, Utc(9, 6, 9));
        await using (var ctx = _db.NewContext())
        {
            // StudentResetJob: satır soft-delete + defter silinir.
            await ctx.StudentPoints.Where(p => p.StudentId == studentId)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.IsDeleted, true));
            await ctx.StudentDailyXps.Where(d => d.StudentId == studentId).ExecuteDeleteAsync();
        }

        await ApplyAsync(0, Utc(9, 6, 10));  // BadgeService sıfırlama event'i
        await ApplyAsync(15, Utc(9, 6, 11)); // sıfırlama sonrası ilk doğru cevap

        (await LedgerAsync(studentId)).ShouldBe(new Dictionary<DateOnly, int> { [new DateOnly(2026, 9, 6)] = 15 });
        await using var check = _db.NewContext();
        (await check.StudentPoints.SingleAsync(p => p.StudentId == studentId)).XP.ShouldBe(15);
    }

    [Fact]
    public async Task Ledgers_are_per_student()
    {
        var a = await SeedStudentAsync(42);
        var b = await SeedStudentAsync(43);
        await ApplyAsync(10, Utc(9, 6, 8), 42);
        await ApplyAsync(10, Utc(9, 6, 8), 43);
        await ApplyAsync(30, Utc(9, 6, 9), 42);
        await ApplyAsync(11, Utc(9, 6, 9), 43);

        (await LedgerAsync(a))[new DateOnly(2026, 9, 6)].ShouldBe(20);
        (await LedgerAsync(b))[new DateOnly(2026, 9, 6)].ShouldBe(1);
    }

    [Fact]
    public async Task A_student_created_in_the_events_week_gets_the_first_sync_counted()
    {
        // 2026-09-02 Çarşamba; hafta Pazartesi 31 Ağustos. Kayıt Salı açıldı → ilk toplamın tamamı bu haftaya ait.
        var studentId = await SeedStudentAsync(createdAt: Utc(9, 1, 10));
        await ApplyAsync(40, Utc(9, 2, 8));
        await ApplyAsync(55, Utc(9, 2, 9));

        (await LedgerAsync(studentId)).ShouldBe(new Dictionary<DateOnly, int> { [new DateOnly(2026, 9, 2)] = 55 });
    }

    [Fact]
    public async Task A_total_dropping_to_zero_is_a_reset_and_clears_the_ledger_instead_of_a_negative_row()
    {
        var studentId = await SeedStudentAsync();
        await ApplyAsync(100, Utc(9, 6, 8));
        await ApplyAsync(130, Utc(9, 6, 9));
        (await LedgerAsync(studentId)).ShouldNotBeEmpty();

        await ApplyAsync(0, Utc(9, 6, 10)); // BadgeService sıfırlaması (StudentResetJob'dan önce gelebilir)
        (await LedgerAsync(studentId)).ShouldBeEmpty();

        await ApplyAsync(12, Utc(9, 6, 11));
        (await LedgerAsync(studentId)).ShouldBe(new Dictionary<DateOnly, int> { [new DateOnly(2026, 9, 6)] = 12 });
    }

    [Fact]
    public async Task Ledger_additions_saturate_instead_of_overflowing()
    {
        var studentId = await SeedStudentAsync();
        await ApplyAsync(0, Utc(9, 6, 8));
        await using (var ctx = _db.NewContext())
        {
            ctx.StudentDailyXps.Add(new StudentDailyXp { StudentId = studentId, Day = new DateOnly(2026, 9, 6), Xp = int.MaxValue - 5 });
            await ctx.SaveChangesAsync();
        }

        await ApplyAsync(1_000, Utc(9, 6, 9));

        (await LedgerAsync(studentId))[new DateOnly(2026, 9, 6)].ShouldBe(int.MaxValue);
    }
}
