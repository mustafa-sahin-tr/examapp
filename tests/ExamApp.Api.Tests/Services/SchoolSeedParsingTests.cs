using System.Text;
using ExamApp.Api.Services.Schools.Seed;

namespace ExamApp.Api.Tests.Services;

/// <summary>seed-schools (issue #216): CSV parse, ad normalizasyonu ve komut satırı çözümleme.</summary>
public class SchoolSeedParsingTests
{
    // ---- CSV ----

    [Fact]
    public void Csv_parses_bom_crlf_quoted_fields_and_turkish_characters()
    {
        var csv =
            "il,ilce,okul_adi,kurum_kodu,okul_turu,web_sitesi,adres,telefon,harita,cekim_tarihi\r\n" +
            "Ankara,Çankaya,\"Atatürk Kültür, Dil ve Tarih Yüksek Kurumu İlkokulu\",775804,İlkokul,https://x,,,https://y,2026-08-31\r\n" +
            "Hatay,Antakya,\"Şehit \"\"Adem\"\" Döğüşğen\r\nOrtaokulu\",123,Ortaokul,,,,,2026-08-31\r\n" +
            "\r\n" +
            "Kars,Sarıkamış,Iğdır Caddesi İlkokulu,456,İlkokul,,,,,2026-08-31\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();

        var rows = MebSchoolCsv.Parse(new MemoryStream(bytes));

        rows.Count.ShouldBe(3);
        rows[0].ShouldBe(new MebSchoolRow("Ankara", "Çankaya", "Atatürk Kültür, Dil ve Tarih Yüksek Kurumu İlkokulu", "775804", "İlkokul"));
        rows[1].Name.ShouldBe("Şehit \"Adem\" Döğüşğen\r\nOrtaokulu");
        rows[1].SchoolType.ShouldBe("Ortaokul");
        rows[2].ShouldBe(new MebSchoolRow("Kars", "Sarıkamış", "Iğdır Caddesi İlkokulu", "456", "İlkokul"));
    }

    [Fact]
    public void Csv_columns_are_found_by_header_name_not_position()
    {
        var csv = "okul_turu,kurum_kodu,il,okul_adi,ilce\nİlkokul,1,Kars,Test İlkokulu,Merkez\n";

        var rows = MebSchoolCsv.Parse(new StringReader(csv));

        rows.ShouldHaveSingleItem().ShouldBe(new MebSchoolRow("Kars", "Merkez", "Test İlkokulu", "1", "İlkokul"));
    }

    [Fact]
    public void Csv_missing_required_column_throws_with_column_name()
    {
        var csv = "il,ilce,okul_adi,okul_turu\nKars,Merkez,X,İlkokul\n";

        var ex = Should.Throw<InvalidDataException>(() => MebSchoolCsv.Parse(new StringReader(csv)));

        ex.Message.ShouldContain("kurum_kodu");
    }

    [Fact]
    public void Csv_empty_input_throws()
    {
        Should.Throw<InvalidDataException>(() => MebSchoolCsv.Parse(new StringReader("")));
    }

    // ---- normalizasyon ----

    [Theory]
    [InlineData("KARATAŞ İLKOKULU", "Karataş İlkokulu")]
    [InlineData("ŞEHİT ÖĞRETMEN ALİ YILDIRIM ORTAOKULU", "Şehit Öğretmen Ali Yıldırım Ortaokulu")]
    [InlineData("FATMA-YUSUF BİLGİÇ İLKOKULU", "Fatma-Yusuf Bilgiç İlkokulu")]
    [InlineData("EMİŞBELENİ İMAM HATİP ORTAOKULU", "Emişbeleni İmam Hatip Ortaokulu")]
    [InlineData("IĞDIR İLKOKULU", "Iğdır İlkokulu")]
    [InlineData("kapıkaya ilkokulu", "Kapıkaya İlkokulu")]
    [InlineData("Kapıkaya ilkokulu", "Kapıkaya İlkokulu")]
    [InlineData("Şehit Selami Akça imam Hatip Ortaokulu", "Şehit Selami Akça İmam Hatip Ortaokulu")]
    [InlineData("  Atatürk   Ortaokulu ", "Atatürk Ortaokulu")]
    // Karışık yazımda kısaltma ve soyad korunur
    [InlineData("TOKİ Şehit Suat Ocak İlkokulu", "TOKİ Şehit Suat Ocak İlkokulu")]
    [InlineData("T.E.K. İlkokulu", "T.E.K. İlkokulu")]
    [InlineData("Ahmet KABAKLI Ortaokulu", "Ahmet KABAKLI Ortaokulu")]
    [InlineData("II. Abdülhamid Han Ortaokulu", "II. Abdülhamid Han Ortaokulu")]
    [InlineData("Dr.Hüseyin Vural İlkokulu", "Dr.Hüseyin Vural İlkokulu")]
    public void Normalize_applies_turkish_title_case_rules(string raw, string expected)
    {
        SchoolNameNormalizer.Normalize(raw).ShouldBe(expected);
    }

    [Theory]
    [InlineData("İlkokul", false, SchoolSeedKind.Ilkokul)]
    [InlineData("ilkokul", false, SchoolSeedKind.Ilkokul)]
    [InlineData("Ortaokul", false, SchoolSeedKind.Ortaokul)]
    [InlineData("İmam Hatip Ortaokulu", false, null)]
    [InlineData("İmam Hatip Ortaokulu", true, SchoolSeedKind.Ortaokul)]
    [InlineData("Anadolu Lisesi", true, null)]
    [InlineData("Anaokulu", true, null)]
    public void ClassifyKind_maps_source_types(string type, bool includeImamHatip, SchoolSeedKind? expected)
    {
        SchoolSeedService.ClassifyKind(type, includeImamHatip).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Millî Eğitim Vakfı Gökkuşağı Özel Eğitim Ortaokulu", true)]
    [InlineData("Dr.Hüseyin Vural ÖZEL EĞİTİM İlkokulu", true)]
    [InlineData("Çankaya Uygulama Merkezi Ortaokulu", true)]
    [InlineData("Özel Doğa Koleji İlkokulu", false)]
    [InlineData("Atatürk İlkokulu", false)]
    public void IsSpecialEducation_detects_misclassified_rows(string name, bool expected)
    {
        SchoolSeedService.IsSpecialEducation(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("İstanbul", "ISTANBUL")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("Iğdır", "IĞDIR")]
    [InlineData("Iğdır", "ığdır")]
    [InlineData("Şişli", " ŞİŞLİ ")]
    public void FoldKey_is_turkish_case_and_dotted_i_insensitive(string a, string b)
    {
        SchoolSeedService.FoldKey(a).ShouldBe(SchoolSeedService.FoldKey(b));
    }

    // ---- komut satırı ----

    [Fact]
    public void Command_defaults_match_issue_scope()
    {
        var cmd = SchoolSeedCommand.Parse(["seed-schools"]);

        cmd.Options.Provinces.ShouldBe(SchoolSeedOptions.DefaultProvinces);
        cmd.Options.Provinces.Count.ShouldBe(10);
        cmd.Options.LimitPerProvince.ShouldBeNull();
        cmd.Options.Kinds.ShouldBe(new[] { SchoolSeedKind.Ilkokul, SchoolSeedKind.Ortaokul }, ignoreOrder: true);
        cmd.Options.IncludeImamHatip.ShouldBeFalse();
        cmd.Options.DryRun.ShouldBeFalse();
        cmd.ConnectionString.ShouldBeNull();
        cmd.ShowHelp.ShouldBeFalse();
    }

    [Fact]
    public void Command_parses_all_options()
    {
        var cmd = SchoolSeedCommand.Parse(
        [
            "seed-schools", "--provinces", "Kars, Erzincan", "--limit", "5", "--types", "Ortaokul",
            "--include-imam-hatip", "--dry-run", "--connection", "Host=x;Database=y"
        ]);

        cmd.Options.Provinces.ShouldBe(new[] { "Kars", "Erzincan" });
        cmd.Options.LimitPerProvince.ShouldBe(5);
        cmd.Options.Kinds.ShouldBe(new[] { SchoolSeedKind.Ortaokul });
        cmd.Options.IncludeImamHatip.ShouldBeTrue();
        cmd.Options.DryRun.ShouldBeTrue();
        cmd.ConnectionString.ShouldBe("Host=x;Database=y");
    }

    [Theory]
    [InlineData("--limit", "0")]
    [InlineData("--limit", "abc")]
    [InlineData("--types", "lise")]
    [InlineData("--bogus", "1")]
    public void Command_rejects_invalid_options(string option, string value)
    {
        Should.Throw<ArgumentException>(() => SchoolSeedCommand.Parse(["seed-schools", option, value]));
    }

    [Fact]
    public void Command_rejects_empty_province_list()
    {
        Should.Throw<ArgumentException>(() => SchoolSeedCommand.Parse(["seed-schools", "--provinces", ""]));
        Should.Throw<ArgumentException>(() => SchoolSeedCommand.Parse(["seed-schools", "--provinces", " , "]));
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    public void Command_help_flag_is_recognised(string flag)
    {
        var cmd = SchoolSeedCommand.Parse(["seed-schools", flag]);

        cmd.ShowHelp.ShouldBeTrue();
        SchoolSeedCommand.Usage.ShouldContain("--no-migrate");
        SchoolSeedCommand.Usage.ShouldContain("--dry-run");
    }

    [Fact]
    public void Command_parses_no_migrate()
    {
        SchoolSeedCommand.Parse(["seed-schools", "--no-migrate"]).NoMigrate.ShouldBeTrue();
        SchoolSeedCommand.Parse(["seed-schools"]).NoMigrate.ShouldBeFalse();
    }

    [Fact]
    public void Command_keeps_duplicate_provinces_in_args_but_service_dedups_them()
    {
        // Parse yalnızca böler; tekrarları DistinctBy(FoldKey) ile servis atar (il sırası korunur).
        var cmd = SchoolSeedCommand.Parse(["seed-schools", "--provinces", "Kars,kars,KARS,Erzincan"]);

        cmd.Options.Provinces.ShouldBe(new[] { "Kars", "kars", "KARS", "Erzincan" });
        cmd.Options.Provinces.Select(SchoolSeedService.FoldKey).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void Command_rejects_option_without_value()
    {
        Should.Throw<ArgumentException>(() => SchoolSeedCommand.Parse(["seed-schools", "--provinces", "--dry-run"]));
        Should.Throw<ArgumentException>(() => SchoolSeedCommand.Parse(["seed-schools", "--limit"]));
    }

    [Fact]
    public void Command_is_requested_only_by_first_argument()
    {
        SchoolSeedCommand.IsRequested(["seed-schools"]).ShouldBeTrue();
        SchoolSeedCommand.IsRequested(["SEED-SCHOOLS", "--dry-run"]).ShouldBeTrue();
        SchoolSeedCommand.IsRequested([]).ShouldBeFalse();
        SchoolSeedCommand.IsRequested(["--urls", "http://localhost:5079"]).ShouldBeFalse();
    }
}
