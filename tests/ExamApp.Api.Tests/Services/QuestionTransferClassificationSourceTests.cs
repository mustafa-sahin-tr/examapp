using System.Reflection;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Services.QuestionTransfer;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #577 (security L5): aktarım içe alımında tanımsız sayısal/sayısal-string kaynak saklanmaz.</summary>
public class QuestionTransferClassificationSourceTests
{
    private static ClassificationSource? Parse(string json)
    {
        var method = typeof(QuestionTransferJobRunner).GetMethod("ParseClassificationSource", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var doc = JsonDocument.Parse(json);
        return (ClassificationSource?)method.Invoke(null, [doc.RootElement]);
    }

    [Theory]
    [InlineData("""{"classificationSource":0}""", ClassificationSource.Human)]
    [InlineData("""{"classificationSource":2}""", ClassificationSource.Book)]
    [InlineData("""{"classificationSource":"Book"}""", ClassificationSource.Book)]
    [InlineData("""{"classificationSource":"ai"}""", ClassificationSource.AI)]
    public void Defined_values_are_parsed(string json, ClassificationSource expected) => Parse(json).ShouldBe(expected);

    [Theory]
    [InlineData("""{"classificationSource":99}""")]
    [InlineData("""{"classificationSource":-1}""")]
    [InlineData("""{"classificationSource":"99"}""")]
    [InlineData("""{"classificationSource":"Robot"}""")]
    [InlineData("""{}""")]
    public void Undefined_values_are_dropped(string json) => Parse(json).ShouldBeNull();
}
