using ExamApp.Api.Services.Questions;

namespace ExamApp.Api.Tests.Services;

// Issue #590: görseli olmayan sorunun sınıflandırıcıya giden düz metni.
public class QuestionClassificationTextTests
{
    [Fact]
    public void Build_joins_passage_instruction_stem_answers_and_plan_labels_without_html()
    {
        const string plan = """{"version":1,"type":"matrixChoice","columns":[{"id":"cD","label":"D"},{"id":"cY","label":"Y"}],"rows":[{"id":"r1","label":"Su 100 °C'de kaynar."}]}""";

        var text = QuestionClassificationText.Build(
            passageTitle: null, passageText: "<p>Okuma&nbsp;parçası</p>", instruction: "Doğru mu, yanlış mı?",
            questionText: "<p><b>Soru</b> kökü</p>", questionSubText: null, answerTexts: ["A şıkkı", null, " "],
            interactionPlan: plan);

        text.ShouldBe("Okuma parçası\nDoğru mu, yanlış mı?\nSoru kökü\nA şıkkı\nD\nY\nSu 100 °C'de kaynar.");
    }

    [Fact]
    public void Build_ignores_structural_plan_fields_and_never_reads_a_solution_key()
    {
        // Eski satırda plan içinde çözüm kalmış olsa bile yalnız label/text değerleri alınır; id'ler alınmaz.
        const string plan = """{"type":"dragPlace","targets":[{"id":"t1","label":"Memeliler","capacity":2}],"cards":[{"id":"k1","label":"Yunus"}],"solution":{"placements":[{"targetId":"t1","cardId":"k1"}]}}""";

        QuestionClassificationText.Build(null, null, null, null, null, [], plan).ShouldBe("Memeliler\nYunus");
    }

    [Fact]
    public void Build_includes_layout_text_with_blanks_instead_of_placeholders()
    {
        const string plan = """{"type":"slotFill","layout":{"kind":"table","header":["Ülke","Başkent"],"rows":[["Türkiye","[[s1]]"]]},"slots":[{"id":"s1","input":{"kind":"text"}}]}""";
        QuestionClassificationText.Build(null, null, null, null, null, [], plan).ShouldBe("Ülke | Başkent\nTürkiye | ____");

        const string cloze = """{"type":"dragPlace","layout":{"kind":"text","text":"Su [[t1]] derecede donar."},"targets":[{"id":"t1"}],"cards":[{"id":"k1","label":"0"}]}""";
        QuestionClassificationText.Build(null, null, null, null, null, [], cloze).ShouldBe("Su ____ derecede donar.\n0");
    }

    [Fact]
    public void Build_tolerates_a_broken_plan_and_caps_the_length()
    {
        QuestionClassificationText.Build(null, null, null, "kök", null, [], "{broken").ShouldBe("kök");
        QuestionClassificationText.Build(null, null, null, new string('a', 5000), null, [], null)
            .Length.ShouldBe(QuestionClassificationText.MaxLength);
    }
}
