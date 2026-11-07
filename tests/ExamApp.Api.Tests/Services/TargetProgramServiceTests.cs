using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.TargetGoals;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #695: hedef kataloğu ve CSV aktarımı. Veriler sentetiktir, gerçek taban puan değildir.</summary>
public class TargetProgramServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static TargetProgramImport Lgs(string csv, Encoding? encoding = null) =>
        new(TargetScoreTypes.LgsExam, 2026, "test source", (encoding ?? Encoding.UTF8).GetBytes(csv));

    private static TargetProgramImport Yks(string csv) =>
        new(TargetScoreTypes.YksExam, 2026, "test source", Encoding.UTF8.GetBytes(csv));

    private async Task<TargetProgramImportPreviewDto> PreviewAsync(TargetProgramImport import)
    {
        await using var ctx = _db.NewContext();
        return await new TargetProgramService(ctx).PreviewImportAsync(import);
    }

    private async Task<TargetProgramImportResultDto> ConfirmAsync(TargetProgramImport import)
    {
        await using var ctx = _db.NewContext();
        return await new TargetProgramService(ctx).ConfirmImportAsync(import, 1);
    }

    private async Task<List<TargetProgram>> RowsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.TargetPrograms.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
    }

    private static SaveTargetProgramDto Save(string name = "Örnek Lisesi", string scoreType = TargetScoreTypes.Lgs, string? institution = null) => new()
    {
        ScoreType = scoreType, Name = name, Institution = institution, City = "İstanbul", Year = 2026, MinimumScore = 450, SourceReference = "test source",
    };

    [Fact]
    public async Task Summary_counts_each_exam_and_its_latest_year()
    {
        await ConfirmAsync(Lgs("Okul;İl;Taban Puan\nBir Lisesi;Ankara;400\nİki Lisesi;İzmir;410\n"));
        await ConfirmAsync(Lgs("Okul;İl;Taban Puan\nEski Lisesi;Ankara;390\n") with { Year = 2025 });

        await using var ctx = _db.NewContext();
        var summary = await new TargetProgramService(ctx).SummaryAsync();

        var lgs = summary.Single(s => s.Exam == TargetScoreTypes.LgsExam);
        (lgs.Total, lgs.LatestYear, lgs.LatestYearCount).ShouldBe((3, 2026, 2));
        var yks = summary.Single(s => s.Exam == TargetScoreTypes.YksExam);
        (yks.Total, yks.LatestYear, yks.LatestYearCount).ShouldBe((0, (int?)null, 0));
    }

    /// <summary>Taban puan ekranının indirttiği örnek dosyalar (#900, SAMPLE_CSV) hatasız okunmalı.</summary>
    [Fact]
    public async Task Admin_sample_files_are_read_without_errors()
    {
        var lgs = await PreviewAsync(Lgs("Okul;İl;İlçe;Taban Puan;Yüzdelik\nÖrnek Fen Lisesi;İstanbul;Kadıköy;478,20;0,15\nÖrnek Anadolu Lisesi;Ankara;Çankaya;452,70;2,40\n"));
        var yks = await PreviewAsync(Yks("Program;Üniversite;İl;Puan Türü;Taban Puan;Yüzdelik\nBilgisayar Mühendisliği;Örnek Üniversitesi;Ankara;SAY;480,50;1,20\nHukuk;Örnek Üniversitesi;İstanbul;EA;455,30;3,10\n"));

        (lgs.NewCount, lgs.InvalidCount).ShouldBe((2, 0));
        (yks.NewCount, yks.InvalidCount).ShouldBe((2, 0));
    }

    [Fact]
    public async Task Preview_classifies_rows_and_writes_nothing()
    {
        await ConfirmAsync(Lgs("Okul;İl;İlçe;Taban Puan;Yüzdelik\nDeğişen Lisesi;Ankara;Çankaya;400;5\nAynı Lisesi;Ankara;Çankaya;410;4\n"));

        var preview = await PreviewAsync(Lgs(
            "Okul;İl;İlçe;Taban Puan;Yüzdelik\n" +
            "Değişen Lisesi;Ankara;Çankaya;420,5;3\n" +
            "Aynı Lisesi;ANKARA;çankaya;410;4\n" +
            "Yeni Lisesi;İzmir;;430.25;\n" +
            "Puansız Lisesi;İzmir;;Dolmadı;\n" +
            "Yeni Lisesi;izmir;;431;\n"));

        (preview.NewCount, preview.ChangedCount, preview.UnchangedCount, preview.InvalidCount).ShouldBe((1, 1, 1, 2));
        preview.New.ShouldHaveSingleItem().MinimumScore.ShouldBe(430.25m);
        var change = preview.Changed.ShouldHaveSingleItem();
        (change.PreviousMinimumScore, change.MinimumScore, change.PreviousPercentile, change.Percentile).ShouldBe((400m, 420.5m, 5m, 3m));
        preview.Invalid.Select(e => (e.Line, e.Code)).ShouldBe([(5, "MinimumScoreInvalid"), (6, "Duplicate")]);

        (await RowsAsync()).Count.ShouldBe(2);
        (await RowsAsync()).Single(p => p.Name == "Değişen Lisesi").MinimumScore.ShouldBe(400m);
    }

    [Fact]
    public async Task Confirm_writes_valid_rows_skips_invalid_ones_and_a_second_run_changes_nothing()
    {
        const string csv = "Okul,İl,Taban Puan\nA Lisesi,Bursa,450\nB Lisesi,Bursa,600\n\"C, Fen Lisesi\",Bursa,470\n";

        var first = await ConfirmAsync(Lgs(csv));
        (first.Created, first.Updated, first.Unchanged, first.Skipped).ShouldBe((2, 0, 0, 1));

        var rows = await RowsAsync();
        rows.Select(p => p.Name).ShouldBe(["A Lisesi", "C, Fen Lisesi"]);
        rows[0].ScoreType.ShouldBe(TargetScoreTypes.Lgs);
        rows[0].Year.ShouldBe(2026);
        rows[0].SourceReference.ShouldBe("test source");

        var second = await ConfirmAsync(Lgs(csv));
        (second.Created, second.Updated, second.Unchanged, second.Skipped).ShouldBe((0, 0, 2, 1));
        (await RowsAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Another_year_is_a_separate_row()
    {
        await ConfirmAsync(Lgs("Okul;İl;Taban Puan\nA Lisesi;Bursa;450\n"));
        await ConfirmAsync(Lgs("Okul;İl;Taban Puan\nA Lisesi;Bursa;460\n") with { Year = 2025 });

        (await RowsAsync()).Select(p => (p.Year, p.MinimumScore)).ShouldBe([(2026, 450m), (2025, 460m)]);
    }

    [Fact]
    public async Task Excel_output_in_windows_1254_is_read_correctly()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        await ConfirmAsync(Lgs("Okul;İl;Taban Puan\r\nÇağdaş Şişli Lisesi;İstanbul;450\r\n", Encoding.GetEncoding(1254)));

        var row = (await RowsAsync()).ShouldHaveSingleItem();
        (row.Name, row.City).ShouldBe(("Çağdaş Şişli Lisesi", "İstanbul"));
    }

    [Fact]
    public async Task Yks_rows_need_a_supported_score_type_and_an_institution()
    {
        var preview = await PreviewAsync(Yks(
            "Program;Üniversite;İl;Puan Türü;Taban Puan\n" +
            "Bilgisayar Mühendisliği;Örnek Üniversitesi;Ankara;SAY;480\n" +
            "Türk Dili;Örnek Üniversitesi;Ankara;SÖZ;350\n" +
            "Mütercim;Örnek Üniversitesi;Ankara;DİL;400\n" +
            "Hukuk;;Ankara;EA;450\n" +
            "Tarih;Örnek Üniversitesi;Ankara;;300\n"));

        preview.New.Select(row => row.ScoreType).ShouldBe([TargetScoreTypes.Say, TargetScoreTypes.Soz]);
        preview.Invalid.Select(e => e.Code).ShouldBe(["ScoreTypeInvalid", "InstitutionRequired", "ScoreTypeInvalid"]);
    }

    [Theory]
    [InlineData("Ad;Kurum\nA;B\n")]
    [InlineData("")]
    public async Task A_file_without_the_required_columns_is_refused(string csv)
    {
        var error = await Should.ThrowAsync<TargetGoalRuleException>(() => PreviewAsync(Lgs(csv)));
        error.Message.ShouldBe("CsvHeaderMissing");
    }

    [Fact]
    public async Task Preview_lists_are_capped_but_counts_are_exact()
    {
        var csv = new StringBuilder("Okul;İl;Taban Puan\n");
        for (var i = 0; i < TargetProgramService.PreviewLimit + 5; i++)
            csv.Append($"Lise {i};Bursa;450\n");

        var preview = await PreviewAsync(Lgs(csv.ToString()));

        preview.NewCount.ShouldBe(TargetProgramService.PreviewLimit + 5);
        preview.New.Count.ShouldBe(TargetProgramService.PreviewLimit);
    }

    [Fact]
    public async Task A_second_row_with_the_same_identity_is_refused_ignoring_turkish_case()
    {
        await using var ctx = _db.NewContext();
        var service = new TargetProgramService(ctx);
        await service.CreateAsync(Save("Işık Lisesi"), 1);

        var error = await Should.ThrowAsync<TargetGoalRuleException>(() => service.CreateAsync(Save("IŞIK LİSESİ"), 1));
        error.Message.ShouldBe("Duplicate");
    }

    [Fact]
    public async Task Search_matches_without_turkish_letters_and_only_within_the_exam()
    {
        await using var ctx = _db.NewContext();
        var service = new TargetProgramService(ctx);
        await service.CreateAsync(Save("Çağdaş Şişli Lisesi"), 1);
        await service.CreateAsync(Save("Çağdaş Program", TargetScoreTypes.Say, "Örnek Üniversitesi"), 1);

        var page = await service.SearchAsync(new(TargetScoreTypes.LgsExam, null, null, null, "cagdas sisli", 0, 20));

        page.Total.ShouldBe(1);
        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Çağdaş Şişli Lisesi");
        (await service.SearchAsync(new(null, null, null, null, "istanbul", 0, 20))).Total.ShouldBe(2);
    }

    [Fact]
    public async Task Empty_search_lists_most_chosen_first_and_cities_are_listed_per_exam_issue891()
    {
        await using var ctx = _db.NewContext();
        var service = new TargetProgramService(ctx);
        var low = await service.CreateAsync(Save("Az Seçilen Lisesi"), 1);
        var popular = await service.CreateAsync(Save("Çok Seçilen Lisesi"), 1);
        var ankara = Save("Ankara Lisesi");
        ankara.City = "Ankara";
        await service.CreateAsync(ankara, 1);
        var izmir = Save("İzmir Programı", TargetScoreTypes.Say, "Örnek Üniversitesi");
        izmir.City = "İzmir";
        await service.CreateAsync(izmir, 1);
        var grade = new Grade { Name = "8" };
        ctx.Add(grade);
        await ctx.SaveChangesAsync();
        for (var i = 0; i < 2; i++)
        {
            var student = new Student { UserId = 10 + i, StudentNumber = $"S{i}", SchoolName = "s", GradeId = grade.Id };
            ctx.Add(student);
            await ctx.SaveChangesAsync();
            ctx.Add(new StudentTargetGoal { StudentId = student.Id, TargetProgramId = popular.Id, SelectedAt = DateTime.UtcNow });
        }
        await ctx.SaveChangesAsync();

        var suggestions = await service.SearchAsync(new(TargetScoreTypes.LgsExam, null, null, "İstanbul", null, 0, 20));
        suggestions.Items.Select(p => p.Id).ShouldBe([popular.Id, low.Id]);
        // Aramada eski ad sırası.
        (await service.SearchAsync(new(TargetScoreTypes.LgsExam, null, null, "İstanbul", "lisesi", 0, 20))).Items.Select(p => p.Id)
            .ShouldBe([low.Id, popular.Id]);

        (await service.CitiesAsync(TargetScoreTypes.LgsExam)).ShouldBe(["Ankara", "İstanbul"]);
        (await service.CitiesAsync(null)).ShouldBe(["Ankara", "İstanbul", "İzmir"]);
    }

    [Theory]
    [InlineData("5-sinif", 5)]
    [InlineData("12. Sınıf", 12)]
    [InlineData("8", 8)]
    [InlineData("hazirlik", null)]
    [InlineData(null, null)]
    public void Grade_level_is_read_from_the_grade_code_issue891(string? code, int? expected) =>
        TargetGoalService.GradeLevelOf(code).ShouldBe(expected);

    [Fact]
    public async Task A_program_someone_chose_cannot_be_deleted_but_can_be_corrected()
    {
        await using var ctx = _db.NewContext();
        var service = new TargetProgramService(ctx);
        var program = await service.CreateAsync(Save(), 1);
        var grade = new Grade { Name = "8" };
        ctx.Add(grade);
        await ctx.SaveChangesAsync();
        var student = new Student { UserId = 5, StudentNumber = "S5", SchoolName = "s", GradeId = grade.Id };
        ctx.Add(student);
        await ctx.SaveChangesAsync();
        ctx.Add(new StudentTargetGoal { StudentId = student.Id, TargetProgramId = program.Id, SelectedAt = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var error = await Should.ThrowAsync<TargetGoalRuleException>(() => service.DeleteAsync(program.Id, 1));
        error.Message.ShouldBe("InUse");

        var request = Save();
        request.MinimumScore = 455;
        var updated = await service.UpdateAsync(program.Id, request, 1);
        (updated!.MinimumScore, updated.InUse).ShouldBe((455m, true));
    }

    [Fact]
    public async Task A_deleted_program_can_be_added_again()
    {
        await using var ctx = _db.NewContext();
        var service = new TargetProgramService(ctx);
        var program = await service.CreateAsync(Save(), 1);

        (await service.DeleteAsync(program.Id, 1)).ShouldBeTrue();

        (await service.CreateAsync(Save(), 1)).Id.ShouldNotBe(program.Id);
    }
}
