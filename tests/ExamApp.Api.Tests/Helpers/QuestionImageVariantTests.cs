using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// issue #365 (S2): <see cref="QuestionImageVariant.ToV2"/> UI'daki (question-canvas-view-v5) regex'le birebir aynı
/// sonucu üretmeli — UI artık imzalı URL üzerinde yol değiştiremeyeceği için v2 sunucuda türetilip imzalanır.
/// </summary>
public class QuestionImageVariantTests
{
    [Theory]
    [InlineData("/img/exam-questions/questions/0b9c/question.jpg", "/img/exam-questions/questions/0b9c/question-v2.jpg")]
    [InlineData("/img/exam-questions/questions/0b9c/Question.PNG", "/img/exam-questions/questions/0b9c/question-v2.PNG")]
    [InlineData("/img/exam-questions/questions/0b9c/question", "/img/exam-questions/questions/0b9c/question-v2")]
    public void Question_file_maps_to_its_v2_sibling(string stored, string expected)
    {
        QuestionImageVariant.ToV2(stored).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/img/exam-questions/questions/12/7f3c-guid.jpg")] // CreateOrUpdate ile yüklenen: v2 yok
    [InlineData("/img/exam-questions/questions/0b9c/question.jpg?X-Amz-Signature=abc")]
    public void Non_question_files_have_no_v2(string? stored)
    {
        QuestionImageVariant.ToV2(stored).ShouldBeNull();
    }
}
