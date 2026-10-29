using System.Text;
using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #587: <see cref="RegionPaintGrader"/> ızgara modu — kayıt kuralları (boyut, palet, izinli renk, çözüm sayıları), payload
/// doğrulama, renk başına sayı kontrolü (yer serbest), çözüm sızıntısı yok, analiz, çözümsüz soru.
/// </summary>
public class RegionPaintGraderTests
{
    // Matematik Ustası s.167: 10×10 ızgarada 2/5 siyah, 0,35 mavi, %25 yeşil; kırmızı çeldirici (0).
    private const string Plan =
        """{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"black","label":"2/5"},{"id":"p2","color":"blue","label":"0,35"},{"id":"p3","color":"green","label":"%25"},{"id":"p4","color":"red"}]}""";

    private const string Solution = """{"counts":{"p1":40,"p2":35,"p3":25,"p4":0}}""";

    private static readonly RegionPaintGrader Grader = new();

    /// <summary>Hücreleri satır öncelikli sırayla verilen sayılarda palet karakteriyle doldurur.</summary>
    private static string Paint(params (char Paint, int Count)[] runs)
    {
        var flat = new StringBuilder();
        foreach (var (paint, count) in runs)
            flat.Append(paint, count);
        flat.Append('.', 100 - flat.Length);
        var rows = Enumerable.Range(0, 10).Select(r => flat.ToString(r * 10, 10)).ToArray();
        return JsonSerializer.Serialize(new { version = 1, type = "regionPaint", grid = rows });
    }

    private static InteractionGradeInput Input(string? solution, string? payload)
        => new(Plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static List<(string Id, bool Correct)> Rows(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        return doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean())).ToList();
    }

    // ---- Kayıt ----

    [Fact]
    public void ValidateDefinition_Pilot_Accepted()
    {
        Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
    }

    [Theory]
    // Mod yok / bilinmiyor.
    [InlineData("""{"version":1,"type":"regionPaint","rows":10,"cols":10,"palette":[{"id":"p1","color":"black"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"poly","rows":10,"cols":10,"palette":[{"id":"p1","color":"black"}]}""")]
    // Boyut sınır dışı.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":1,"cols":10,"palette":[{"id":"p1","color":"black"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":21,"palette":[{"id":"p1","color":"black"}]}""")]
    // Palet boş / izinsiz renk / aynı renk iki kez / aynı id.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"#ff0000"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"red"},{"id":"p2","color":"red"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"red"},{"id":"p1","color":"blue"}]}""")]
    // Bilinmeyen alan / kontrol karakterli etiket.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"red"}],"x":1}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"red","label":"a‮b"}]}""")]
    public void ValidateDefinition_BadPlan_Rejected(string plan)
    {
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_NineColors_Rejected()
    {
        var colors = new[] { "red", "orange", "yellow", "green", "blue", "purple", "pink", "brown", "black" };
        var palette = colors.Select((c, i) => new { id = $"p{i + 1}", color = c });
        var plan = JsonSerializer.Serialize(new { version = 1, type = "regionPaint", mode = "grid", rows = 10, cols = 10, palette });
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Theory]
    // Renk eksik.
    [InlineData("""{"counts":{"p1":40,"p2":35,"p3":25}}""")]
    // Toplam hücre sayısını aşıyor.
    [InlineData("""{"counts":{"p1":40,"p2":35,"p3":26,"p4":0}}""")]
    // Hepsi 0.
    [InlineData("""{"counts":{"p1":0,"p2":0,"p3":0,"p4":0}}""")]
    // Negatif / ondalık / bilinmeyen renk.
    [InlineData("""{"counts":{"p1":-1,"p2":35,"p3":25,"p4":0}}""")]
    [InlineData("""{"counts":{"p1":40.5,"p2":35,"p3":25,"p4":0}}""")]
    [InlineData("""{"counts":{"p1":40,"p2":35,"p3":25,"p9":0}}""")]
    [InlineData("""{"counts":{"p1":40,"p2":35,"p3":25,"p4":0},"cells":[]}""")]
    public void ValidateDefinition_BadSolution_Rejected(string solution)
    {
        Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();
    }

    // ---- Payload ----

    [Fact]
    public void Validate_Payload()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Paint(('1', 40))).ShouldBeTrue();
        // Palet dışı karakter (5. renk yok), eksik satır, kısa satır.
        Grader.Validate(Plan, Paint(('5', 1))).ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"regionPaint","grid":[".........."]}""").ShouldBeFalse();
        Grader.Validate(Plan, Paint().Replace("\"..........\"]", "\".........\"]")).ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"logicGrid","cells":[]}""").ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyGrid_IsNoAnswer()
    {
        Grader.Normalize(Paint()).ShouldBeNull();
        var painted = Paint(('2', 3));
        Grader.Normalize(painted).ShouldBe(painted);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_CountsMatch_AnyPosition_AllCorrect()
    {
        var result = Grader.Grade(Input(Solution, Paint(('3', 25), ('1', 40), ('2', 35))));
        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(4);
        result.TotalCount.ShouldBe(4);
    }

    [Fact]
    public void Grade_WrongCounts_PerColorRows_NoSolutionLeak()
    {
        // Siyah 40 doğru, mavi 34 eksik, yeşil yok, kırmızı çeldirici kullanılmış.
        var result = Grader.Grade(Input(Solution, Paint(('1', 40), ('2', 34), ('4', 1))));
        result.IsCorrect.ShouldBeFalse();
        Rows(result.Feedback).ShouldBe([("p1", true), ("p2", false), ("p3", false), ("p4", false)]);
        result.Feedback!.ShouldNotContain("35");
        result.Feedback.ShouldNotContain("counts");
    }

    [Fact]
    public void Grade_Blank_OnlyZeroColorsCorrect()
    {
        var result = Grader.Grade(Input(Solution, null));
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(4);
    }

    [Fact]
    public void Grade_NoSolution_Ungradable()
    {
        var result = Grader.Grade(Input(null, Paint(('1', 40))));
        result.TotalCount.ShouldBe(0);
        Grader.Analyze(Input(null, Paint(('1', 40)))).ShouldBeEmpty();
    }

    [Fact]
    public void Analyze_ColorItems_WithPaintedCount()
    {
        var items = Grader.Analyze(Input(Solution, Paint(('1', 40), ('2', 30))));
        items.Select(i => i.ItemId).ShouldBe(["p1", "p2", "p3", "p4"]);
        items[0].ItemLabel.ShouldBe("2/5");
        items[0].Correct.ShouldBeTrue();
        items[1].AnswerKey.ShouldBe("30");
        items[1].Correct.ShouldBeFalse();
        items[2].AnswerKey.ShouldBeNull();
        items[3].ItemLabel.ShouldBe("red");
        items[3].Correct.ShouldBeTrue();
    }

    [Fact]
    public void Registry_ResolvesRegionPaint()
    {
        InteractionGraderRegistry.Default.Resolve("regionpaint").ShouldBeOfType<RegionPaintGrader>();
    }
    // ---- Bölge modu (dilim b) ----

    // "Çift sayılı balonları maviye, tekleri kırmızıya boyayalım": r1, r3 mavi; r2 kırmızı; r4 boş kalmalı. Sarı çeldirici.
    private const string RegionsPlan =
        """{"version":1,"type":"regionPaint","mode":"regions","style":"fill","palette":[{"id":"p1","color":"blue","label":"çift"},{"id":"p2","color":"red"},{"id":"p3","color":"yellow"}],"regions":[{"id":"r1","points":[[0,0],[40,0],[40,40],[0,40]],"label":"12"},{"id":"r2","points":[[50,0],[90,0],[70,40]]},{"id":"r3","points":[[0,50],[40,50],[40,90],[0,90]]},{"id":"r4","points":[[50,50],[90,50],[90,90],[50,90]]}]}""";

    private const string RegionsSolution = """{"fills":{"r1":"p1","r2":"p2","r3":"p1"}}""";

    private static string Fills(params (string Region, string Paint)[] fills)
        => JsonSerializer.Serialize(new { version = 1, type = "regionPaint", fills = fills.ToDictionary(f => f.Region, f => f.Paint) });

    private static InteractionGradeInput RegionsInput(string? solution, string? payload)
        => new(RegionsPlan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    [Fact]
    public void Regions_ValidateDefinition_Pilot_Accepted()
    {
        Grader.ValidateDefinition(RegionsPlan, RegionsSolution).ShouldBeTrue();
        Grader.ValidateDefinition(RegionsPlan, null).ShouldBeTrue();
        Grader.ValidateDefinition(RegionsPlan.Replace("\"fill\"", "\"circle\""), RegionsSolution).ShouldBeTrue();
    }

    [Theory]
    // Bilinmeyen stil / ızgara alanı / bölge yok.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","style":"dots","palette":[{"id":"p1","color":"red"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]]}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","rows":2,"palette":[{"id":"p1","color":"red"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]]}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"regions":[]}""")]
    // İki köşe / doğrusal (alan 0) / negatif / bozuk köşe.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"regions":[{"id":"r1","points":[[0,0],[9,0]]}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"regions":[{"id":"r1","points":[[0,0],[5,5],[9,9]]}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"regions":[{"id":"r1","points":[[-1,0],[9,0],[9,9]]}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9]]}]}""")]
    // Bölge id'si palet id'siyle aynı / iki bölge aynı id / bilinmeyen alan.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"regions":[{"id":"p1","points":[[0,0],[9,0],[9,9]]}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]]},{"id":"r1","points":[[0,0],[9,0],[9,9]]}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]],"box":{}}]}""")]
    public void Regions_ValidateDefinition_BadPlan_Rejected(string plan)
    {
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void Regions_ValidateDefinition_TooManyPoints_Rejected()
    {
        var points = Enumerable.Range(0, 65).Select(i => new[] { 50 + 40 * Math.Cos(i * 0.0966), 50 + 40 * Math.Sin(i * 0.0966) });
        var plan = JsonSerializer.Serialize(new
        {
            version = 1, type = "regionPaint", mode = "regions", palette = new[] { new { id = "p1", color = "red" } },
            regions = new[] { new { id = "r1", points } },
        });
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Theory]
    // Boş / bilinmeyen bölge / bilinmeyen renk / sayı / ızgara çözümü.
    [InlineData("""{"fills":{}}""")]
    [InlineData("""{"fills":{"r9":"p1"}}""")]
    [InlineData("""{"fills":{"r1":"p9"}}""")]
    [InlineData("""{"fills":{"r1":1}}""")]
    [InlineData("""{"counts":{"p1":1}}""")]
    public void Regions_ValidateDefinition_BadSolution_Rejected(string solution)
    {
        Grader.ValidateDefinition(RegionsPlan, solution).ShouldBeFalse();
    }

    [Fact]
    public void Regions_Validate_And_Normalize()
    {
        Grader.Validate(RegionsPlan, Fills(("r1", "p1"))).ShouldBeTrue();
        Grader.Validate(RegionsPlan, Fills(("r9", "p1"))).ShouldBeFalse();
        Grader.Validate(RegionsPlan, Fills(("r1", "p9"))).ShouldBeFalse();
        Grader.Validate(RegionsPlan, Paint(('1', 1))).ShouldBeFalse();
        Grader.Validate(Plan, Fills(("r1", "p1"))).ShouldBeFalse();
        Grader.Normalize(Fills()).ShouldBeNull();
        var painted = Fills(("r4", "p3"));
        Grader.Normalize(painted).ShouldBe(painted);
    }

    [Fact]
    public void Regions_Grade_AllCorrect()
    {
        var result = Grader.Grade(RegionsInput(RegionsSolution, Fills(("r1", "p1"), ("r2", "p2"), ("r3", "p1"))));
        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
    }

    [Fact]
    public void Regions_Grade_ColorAndExtra_OnlyPaintedRows_NoSolutionLeak()
    {
        // r1 doğru, r2 yanlış renk, r3 boyanmamış (eksik), r4 fazla.
        var result = Grader.Grade(RegionsInput(RegionsSolution, Fills(("r1", "p1"), ("r2", "p1"), ("r4", "p1"))));
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(4);
        using var doc = JsonDocument.Parse(result.Feedback!);
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString(), r.GetProperty("correct").GetBoolean(),
                r.TryGetProperty("reason", out var reason) ? reason.GetString() : null)).ToList();
        rows.ShouldBe([("r1", true, null), ("r2", false, "color"), ("r4", false, "extra")]);
        doc.RootElement.GetProperty("found").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("missing").GetInt32().ShouldBe(2);
        doc.RootElement.GetProperty("extra").GetInt32().ShouldBe(1);
        result.Feedback!.ShouldNotContain("r3");
        result.Feedback.ShouldNotContain("p2");
    }

    [Fact]
    public void Regions_Grade_TooManyExtra_HidesRows()
    {
        var plan = JsonSerializer.Serialize(new
        {
            version = 1, type = "regionPaint", mode = "regions", palette = new[] { new { id = "p1", color = "red" } },
            regions = Enumerable.Range(1, 8).Select(i => new { id = $"r{i}", points = new[] { new[] { 0, 0 }, new[] { 9, 0 }, new[] { 9, 9 } } }),
        });
        var payload = Fills(Enumerable.Range(1, 8).Select(i => ($"r{i}", "p1")).ToArray());
        var result = Grader.Grade(new InteractionGradeInput(plan, """{"fills":{"r1":"p1"}}""", payload, null, null));
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(8);
        using var doc = JsonDocument.Parse(result.Feedback!);
        doc.RootElement.GetProperty("rows").GetArrayLength().ShouldBe(0);
        doc.RootElement.GetProperty("tooMany").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public void Regions_Grade_NoSolution_Ungradable()
    {
        Grader.Grade(RegionsInput(null, Fills(("r1", "p1")))).TotalCount.ShouldBe(0);
        Grader.Analyze(RegionsInput(null, Fills(("r1", "p1")))).ShouldBeEmpty();
    }

    [Fact]
    public void Regions_Analyze_TargetItems_WithPaintedColor()
    {
        var items = Grader.Analyze(RegionsInput(RegionsSolution, Fills(("r1", "p1"), ("r2", "p3"), ("r4", "p2"))));
        items.Select(i => i.ItemId).ShouldBe(["r1", "r2", "r3"]);
        items[0].ItemLabel.ShouldBe("12");
        items[0].Correct.ShouldBeTrue();
        items[0].AnswerLabel.ShouldBe("çift");
        items[1].ItemLabel.ShouldBe("2");
        items[1].AnswerKey.ShouldBe("p3");
        items[1].AnswerLabel.ShouldBe("yellow");
        items[1].Correct.ShouldBeFalse();
        items[2].AnswerKey.ShouldBeNull();
    }

    // ---- Numaralı boyama (dilim c) ----

    // TT MatLab s.57 mandala (küçültülmüş): soru 1 → mavi (r1, r3), soru 2 → kırmızı (r2); sarı çeldirici.
    private const string GroupsPlan =
        """{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"blue"},{"id":"p2","color":"red"},{"id":"p3","color":"yellow"}],"groups":[{"id":"g1","label":"1"},{"id":"g2"}],"regions":[{"id":"r1","points":[[0,0],[40,0],[40,40],[0,40]],"group":"g1"},{"id":"r2","points":[[50,0],[90,0],[70,40]],"group":"g2"},{"id":"r3","points":[[0,50],[40,50],[40,90],[0,90]],"group":"g1"}]}""";

    private const string GroupsSolution = """{"fills":{"g1":"p1","g2":"p2"}}""";

    private static InteractionGradeInput GroupsInput(string? solution, string? payload)
        => new(GroupsPlan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    [Fact]
    public void Groups_ValidateDefinition_Pilot_Accepted()
    {
        Grader.ValidateDefinition(GroupsPlan, GroupsSolution).ShouldBeTrue();
        Grader.ValidateDefinition(GroupsPlan, null).ShouldBeTrue();
    }

    [Theory]
    // Grupsuz bölge / bilinmeyen grup / bölgesiz grup / boş grup listesi / grupsuz planda group alanı / grup id'si bölgeyle aynı.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"groups":[{"id":"g1"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]],"group":"g1"},{"id":"r2","points":[[0,0],[9,0],[9,9]]}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"groups":[{"id":"g1"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]],"group":"g9"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"groups":[{"id":"g1"},{"id":"g2"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]],"group":"g1"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"groups":[],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]]}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]],"group":"g1"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"regions","palette":[{"id":"p1","color":"red"}],"groups":[{"id":"r1"}],"regions":[{"id":"r1","points":[[0,0],[9,0],[9,9]],"group":"r1"}]}""")]
    // Izgara modunda gruplar.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":2,"cols":2,"palette":[{"id":"p1","color":"red"}],"groups":[{"id":"g1"}]}""")]
    public void Groups_ValidateDefinition_BadPlan_Rejected(string plan)
    {
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Theory]
    // Eksik grup / bölge anahtarı / bilinmeyen renk.
    [InlineData("""{"fills":{"g1":"p1"}}""")]
    [InlineData("""{"fills":{"g1":"p1","g2":"p2","r1":"p1"}}""")]
    [InlineData("""{"fills":{"g1":"p1","g2":"p9"}}""")]
    public void Groups_ValidateDefinition_BadSolution_Rejected(string solution)
    {
        Grader.ValidateDefinition(GroupsPlan, solution).ShouldBeFalse();
    }

    [Fact]
    public void Groups_Validate_PayloadKeysAreGroups()
    {
        Grader.Validate(GroupsPlan, Fills(("g1", "p1"))).ShouldBeTrue();
        Grader.Validate(GroupsPlan, Fills(("r1", "p1"))).ShouldBeFalse();
    }

    [Fact]
    public void Groups_Grade_PerGroup_UnpaintedIsWrong_NoSolutionLeak()
    {
        Grader.Grade(GroupsInput(GroupsSolution, Fills(("g1", "p1"), ("g2", "p2")))).IsCorrect.ShouldBeTrue();
        var result = Grader.Grade(GroupsInput(GroupsSolution, Fills(("g1", "p3"))));
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(2);
        Rows(result.Feedback).ShouldBe([("g1", false), ("g2", false)]);
        result.Feedback!.ShouldNotContain("p1");
        result.Feedback.ShouldNotContain("p2");
        var partial = Grader.Grade(GroupsInput(GroupsSolution, Fills(("g1", "p1"), ("g2", "p3"))));
        partial.CorrectCount.ShouldBe(1);
        Rows(partial.Feedback).ShouldBe([("g1", true), ("g2", false)]);
    }

    [Fact]
    public void Groups_Analyze_GroupItems()
    {
        var items = Grader.Analyze(GroupsInput(GroupsSolution, Fills(("g2", "p3"))));
        items.Select(i => i.ItemId).ShouldBe(["g1", "g2"]);
        items[0].ItemLabel.ShouldBe("1");
        items[0].AnswerKey.ShouldBeNull();
        items[1].ItemLabel.ShouldBe("2");
        items[1].AnswerLabel.ShouldBe("yellow");
        items[1].Correct.ShouldBeFalse();
    }
}
