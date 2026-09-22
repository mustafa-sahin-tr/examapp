using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// GitHub issue #191 — WorksheetTeacherSharing.SchoolOnly kural motoru (WorksheetAccess).
/// Aynı okul → PublicAssignable ile aynı; farklı okul / okulsuz istekçi / okulsuz sahip → Private gibi.
/// Mevcut Private/PublicView/PublicAssignable davranışı okul parametrelerinden ETKİLENMEZ (regresyon).
/// </summary>
public class WorksheetAccessSchoolOnlyTests
{
    private const int Owner = 5;
    private const int Stranger = 9;
    private const int SchoolA = 100;
    private const int SchoolB = 200;

    // ---- IsSchoolOnlyMatch: tek kural kaynağı ----

    [Theory]
    [InlineData(WorksheetTeacherSharing.SchoolOnly, SchoolA, SchoolA, true)]   // aynı okul
    [InlineData(WorksheetTeacherSharing.SchoolOnly, SchoolB, SchoolA, false)]  // farklı okul
    [InlineData(WorksheetTeacherSharing.SchoolOnly, null, SchoolA, false)]     // okulsuz istekçi
    [InlineData(WorksheetTeacherSharing.SchoolOnly, SchoolA, null, false)]     // okulsuz sahip
    [InlineData(WorksheetTeacherSharing.SchoolOnly, null, null, false)]        // ikisi de okulsuz: null==null eşleşme SAYILMAZ
    [InlineData(WorksheetTeacherSharing.PublicAssignable, SchoolA, SchoolA, false)] // yalnız SchoolOnly için anlamlı
    [InlineData(WorksheetTeacherSharing.PublicView, SchoolA, SchoolA, false)]
    [InlineData(WorksheetTeacherSharing.Private, SchoolA, SchoolA, false)]
    [InlineData(null, SchoolA, SchoolA, false)]
    public void IsSchoolOnlyMatch_Matrix(WorksheetTeacherSharing? sharing, int? requesterSchoolId, int? ownerSchoolId, bool expected)
    {
        WorksheetAccess.IsSchoolOnlyMatch(sharing, requesterSchoolId, ownerSchoolId).ShouldBe(expected);
    }

    // ---- CanView / CanAssign / CanCopy — SchoolOnly, sahibi olmayan öğretmen ----

    [Theory]
    [InlineData(SchoolA, SchoolA, true)]
    [InlineData(SchoolB, SchoolA, false)]
    [InlineData(null, SchoolA, false)]
    [InlineData(SchoolA, null, false)]
    [InlineData(null, null, false)]
    public void CanView_SchoolOnly_NonOwner_DependsOnSchoolMatch(int? requesterSchoolId, int? ownerSchoolId, bool expected)
    {
        WorksheetAccess.CanView(Owner, Stranger, isAdmin: false, WorksheetTeacherSharing.SchoolOnly, null, requesterSchoolId, ownerSchoolId)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData(SchoolA, SchoolA, true)]
    [InlineData(SchoolB, SchoolA, false)]
    [InlineData(null, SchoolA, false)]
    [InlineData(SchoolA, null, false)]
    [InlineData(null, null, false)]
    public void CanAssign_SchoolOnly_NonOwner_DependsOnSchoolMatch(int? requesterSchoolId, int? ownerSchoolId, bool expected)
    {
        WorksheetAccess.CanAssign(Owner, Stranger, isAdmin: false, WorksheetTeacherSharing.SchoolOnly, hasApprovedGrant: false,
                requesterSchoolId, ownerSchoolId)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData(SchoolA, SchoolA, true)]
    [InlineData(SchoolB, SchoolA, false)]
    [InlineData(null, SchoolA, false)]
    [InlineData(SchoolA, null, false)]
    public void CanCopy_SchoolOnly_NonOwner_DependsOnSchoolMatch(int? requesterSchoolId, int? ownerSchoolId, bool expected)
    {
        WorksheetAccess.CanCopy(Owner, Stranger, isAdmin: false, WorksheetTeacherSharing.SchoolOnly, null, requesterSchoolId, ownerSchoolId)
            .ShouldBe(expected);
    }

    [Fact]
    public void SchoolOnly_OwnerAndAdmin_AlwaysTrueRegardlessOfSchool()
    {
        // sahibi (okul bilgisi hiç verilmese bile)
        WorksheetAccess.CanView(Owner, Owner, isAdmin: false, WorksheetTeacherSharing.SchoolOnly).ShouldBeTrue();
        WorksheetAccess.CanAssign(Owner, Owner, isAdmin: false, WorksheetTeacherSharing.SchoolOnly).ShouldBeTrue();
        WorksheetAccess.CanCopy(Owner, Owner, isAdmin: false, WorksheetTeacherSharing.SchoolOnly).ShouldBeTrue();

        // admin, farklı okul
        WorksheetAccess.CanView(Owner, Stranger, isAdmin: true, WorksheetTeacherSharing.SchoolOnly, null, SchoolB, SchoolA).ShouldBeTrue();
        WorksheetAccess.CanAssign(Owner, Stranger, isAdmin: true, WorksheetTeacherSharing.SchoolOnly, false, SchoolB, SchoolA).ShouldBeTrue();
        WorksheetAccess.CanCopy(Owner, Stranger, isAdmin: true, WorksheetTeacherSharing.SchoolOnly, null, SchoolB, SchoolA).ShouldBeTrue();
    }

    [Fact]
    public void SchoolOnly_LegacyOwnerlessWorksheet_NonAdminFalseEvenIfSchoolsMatch()
    {
        WorksheetAccess.CanView(null, Stranger, isAdmin: false, WorksheetTeacherSharing.SchoolOnly, null, SchoolA, SchoolA).ShouldBeFalse();
        WorksheetAccess.CanView(0, Stranger, isAdmin: false, WorksheetTeacherSharing.SchoolOnly, null, SchoolA, SchoolA).ShouldBeFalse();
        WorksheetAccess.CanAssign(null, Stranger, isAdmin: false, WorksheetTeacherSharing.SchoolOnly, false, SchoolA, SchoolA).ShouldBeFalse();
    }

    [Fact]
    public void SchoolOnly_SameSchool_HasExactlyPublicAssignableSemantics()
    {
        // Görür + atar + kopyalar; ama DÜZENLEYEMEZ — PublicAssignable ile birebir aynı.
        var (v, a, c, m) = (
            WorksheetAccess.CanView(Owner, Stranger, false, WorksheetTeacherSharing.SchoolOnly, null, SchoolA, SchoolA),
            WorksheetAccess.CanAssign(Owner, Stranger, false, WorksheetTeacherSharing.SchoolOnly, false, SchoolA, SchoolA),
            WorksheetAccess.CanCopy(Owner, Stranger, false, WorksheetTeacherSharing.SchoolOnly, null, SchoolA, SchoolA),
            WorksheetAccess.CanModify(Owner, Stranger, false, WorksheetTeacherSharing.SchoolOnly));

        (v, a, c, m).ShouldBe((
            WorksheetAccess.CanView(Owner, Stranger, false, WorksheetTeacherSharing.PublicAssignable),
            WorksheetAccess.CanAssign(Owner, Stranger, false, WorksheetTeacherSharing.PublicAssignable),
            WorksheetAccess.CanCopy(Owner, Stranger, false, WorksheetTeacherSharing.PublicAssignable),
            WorksheetAccess.CanModify(Owner, Stranger, false, WorksheetTeacherSharing.PublicAssignable)));
        m.ShouldBeFalse();
    }

    [Fact]
    public void CanAssign_PureRule_GrantIgnoresSchool_ServiceGatesOnCanView()
    {
        // SAF kural: onaylı grant (issue #13) okuldan bağımsız true döner. Bu tek başına bir yetki
        // DEĞİLDİR — servisler önce CanView kapısından geçer ve farklı okul + SchoolOnly için orada
        // NotFound döner (bkz. WorksheetAssignmentServiceSchoolOnlyTests.DifferentSchoolTeacher_WithApprovedGrant_StillNotFound).
        // Ayrıca SchoolOnly'ye geçişte okul dışı grant'ler zaten iptal edilir (UpdateVisibilityAsync).
        WorksheetAccess.CanAssign(Owner, Stranger, false, WorksheetTeacherSharing.SchoolOnly, hasApprovedGrant: true, SchoolB, SchoolA)
            .ShouldBeTrue();
        WorksheetAccess.CanView(Owner, Stranger, false, WorksheetTeacherSharing.SchoolOnly, null, SchoolB, SchoolA)
            .ShouldBeFalse(); // servis kapısı burada kapanır
    }

    // ---- Regresyon: Private / PublicView / PublicAssignable okul parametrelerinden etkilenmez ----

    // Sınır değerler: yabancı (okul parametrelerinin etki edebileceği tek durum) × üç mevcut mod ×
    // {aynı okul, farklı okul, okulsuz} — sahibi/admin dalları okula hiç bakmadan zaten true döner.
    public static IEnumerable<object?[]> LegacySharingWithSchoolCombos() =>
        from s in new[] { WorksheetTeacherSharing.Private, WorksheetTeacherSharing.PublicView, WorksheetTeacherSharing.PublicAssignable }
        from pair in new (int? r, int? o)[] { (SchoolA, SchoolA), (SchoolB, SchoolA), (null, null) }
        select new object?[] { s, pair.r, pair.o };

    [Theory]
    [MemberData(nameof(LegacySharingWithSchoolCombos))]
    public void ExistingSharingModes_SchoolParametersDoNotChangeResult(
        WorksheetTeacherSharing sharing, int? requesterSchoolId, int? ownerSchoolId)
    {
        WorksheetAccess.CanView(Owner, Stranger, false, sharing, null, requesterSchoolId, ownerSchoolId)
            .ShouldBe(WorksheetAccess.CanView(Owner, Stranger, false, sharing));
        WorksheetAccess.CanAssign(Owner, Stranger, false, sharing, false, requesterSchoolId, ownerSchoolId)
            .ShouldBe(WorksheetAccess.CanAssign(Owner, Stranger, false, sharing));
        WorksheetAccess.CanCopy(Owner, Stranger, false, sharing, null, requesterSchoolId, ownerSchoolId)
            .ShouldBe(WorksheetAccess.CanCopy(Owner, Stranger, false, sharing));
    }

    [Fact]
    public void ExistingSharingModes_OwnerAndAdmin_TrueRegardlessOfSchoolParameters()
    {
        foreach (var s in new[] { WorksheetTeacherSharing.Private, WorksheetTeacherSharing.PublicView, WorksheetTeacherSharing.PublicAssignable })
        {
            WorksheetAccess.CanView(Owner, Owner, false, s, null, SchoolB, SchoolA).ShouldBeTrue();
            WorksheetAccess.CanView(Owner, Stranger, true, s, null, SchoolB, SchoolA).ShouldBeTrue();
        }
    }

    [Fact]
    public void Enum_SchoolOnlyIsValueThree_ExistingValuesUnchanged()
    {
        // DB int saklıyor; mevcut 0/1/2 kayıtları etkilenmez, API sözleşmesi de int.
        ((int)WorksheetTeacherSharing.Private).ShouldBe(0);
        ((int)WorksheetTeacherSharing.PublicView).ShouldBe(1);
        ((int)WorksheetTeacherSharing.PublicAssignable).ShouldBe(2);
        ((int)WorksheetTeacherSharing.SchoolOnly).ShouldBe(3);
    }

    // ---- VisibleToTeacherPredicate: filtre SQL düzeyinde (ToQueryString kanıtı) ----

    [Fact]
    public void VisibleToTeacherPredicate_WithSchool_TranslatesToSqlWithTeachersSubquery()
    {
        using var db = TestDb.Create();
        using var ctx = db.NewContext();

        var sql = ctx.Worksheets.Where(WorksheetAccess.VisibleToTeacherPredicate(ctx, Stranger, SchoolA)).ToQueryString();

        sql.ShouldContain("\"TeacherSharing\" = 3");    // SchoolOnly dalı SQL'de
        sql.ShouldContain("\"Teachers\"");              // sahibin okulu korelasyonlu alt sorguyla
        sql.ShouldContain("\"SchoolId\"");
        sql.ShouldContain("\"TeacherSharing\" = 1");
        sql.ShouldContain("\"TeacherSharing\" = 2");
    }

    [Fact]
    public void VisibleToTeacherPredicate_WithoutSchool_ProducesNoSchoolOnlyBranch()
    {
        using var db = TestDb.Create();
        using var ctx = db.NewContext();

        var sql = ctx.Worksheets.Where(WorksheetAccess.VisibleToTeacherPredicate(ctx, Stranger, null)).ToQueryString();

        sql.ShouldNotContain("\"TeacherSharing\" = 3");
        sql.ShouldNotContain("\"Teachers\"");
        sql.ShouldContain("\"TeacherSharing\" = 1");
        sql.ShouldContain("\"TeacherSharing\" = 2");
    }

    // ---- ResolveSchoolContextAsync: yalnızca gerekince sorgu atar ----

    private static async Task<(int schoolA, int schoolB)> SeedSchoolsAsync(AppDbContext ctx)
    {
        var a = new School { Name = "Okul A" };
        var b = new School { Name = "Okul B" };
        ctx.Schools.AddRange(a, b);
        await ctx.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task ResolveSchoolContextAsync_SchoolOnlyNonOwner_ReadsBothSchoolsFromTeachers()
    {
        using var db = TestDb.Create();
        int schoolA, schoolB;
        await using (var seed = db.NewContext())
        {
            (schoolA, schoolB) = await SeedSchoolsAsync(seed);
            seed.Teachers.AddRange(
                new Teacher { UserId = Owner, SchoolId = schoolA },
                new Teacher { UserId = Stranger, SchoolId = schoolB });
            await seed.SaveChangesAsync();
        }

        await using var ctx = db.NewContext();
        var ws = new Worksheet { CreateUserId = Owner, TeacherSharing = WorksheetTeacherSharing.SchoolOnly };

        var (ownerSchoolId, requesterSchoolId) = await ctx.ResolveSchoolContextAsync(ws, Stranger, isAdmin: false);

        ownerSchoolId.ShouldBe(schoolA);
        requesterSchoolId.ShouldBe(schoolB);
    }

    [Theory]
    [InlineData(WorksheetTeacherSharing.PublicAssignable, Stranger, false)] // SchoolOnly değil
    [InlineData(WorksheetTeacherSharing.SchoolOnly, Owner, false)]          // sahibi
    [InlineData(WorksheetTeacherSharing.SchoolOnly, Stranger, true)]        // admin
    public async Task ResolveSchoolContextAsync_NotNeeded_ReturnsNullsWithoutQuerying(
        WorksheetTeacherSharing sharing, int userId, bool isAdmin)
    {
        using var db = TestDb.Create();
        await using (var seed = db.NewContext())
        {
            var (schoolA, _) = await SeedSchoolsAsync(seed);
            seed.Teachers.AddRange(
                new Teacher { UserId = Owner, SchoolId = schoolA },
                new Teacher { UserId = Stranger, SchoolId = schoolA });
            await seed.SaveChangesAsync();
        }

        var counter = new CommandCounter();
        await using var ctx = db.NewContext(counter);
        var ws = new Worksheet { CreateUserId = Owner, TeacherSharing = sharing };

        var result = await ctx.ResolveSchoolContextAsync(ws, userId, isAdmin);

        result.ShouldBe(((int?)null, (int?)null)); // Teacher satırları dolu olsa da okunmaz — karar okula bakmıyor
        counter.ReaderCount.ShouldBe(0);            // hiç SQL gitmedi
    }

    [Fact]
    public async Task ResolveSchoolContextAsync_Needed_IssuesExactlyOneQuery()
    {
        using var db = TestDb.Create();
        await using (var seed = db.NewContext())
        {
            var (schoolA, schoolB) = await SeedSchoolsAsync(seed);
            seed.Teachers.AddRange(
                new Teacher { UserId = Owner, SchoolId = schoolA },
                new Teacher { UserId = Stranger, SchoolId = schoolB });
            await seed.SaveChangesAsync();
        }

        var counter = new CommandCounter();
        await using var ctx = db.NewContext(counter);
        var ws = new Worksheet { CreateUserId = Owner, TeacherSharing = WorksheetTeacherSharing.SchoolOnly };

        await ctx.ResolveSchoolContextAsync(ws, Stranger, isAdmin: false);

        counter.ReaderCount.ShouldBe(1); // iki Teacher satırı tek sorguda (N+1 yok)
    }

    /// <summary>Gönderilen SELECT komutlarını sayar (DbCommandInterceptor).</summary>
    private sealed class CommandCounter : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public int ReaderCount;

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ReaderCount);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> ReaderExecuting(
            System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result)
        {
            Interlocked.Increment(ref ReaderCount);
            return base.ReaderExecuting(command, eventData, result);
        }
    }
}
