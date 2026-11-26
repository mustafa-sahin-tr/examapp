using System.Collections.Generic;
using System.Text.Json;
using ExamApp.Api.Services.Worksheets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ExamApp.Api.Tests.Services;

public class TestSolveOptionsTests
{
    private static TestSolveOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new TestSolveOptions();
        configuration.GetSection(TestSolveOptions.Section).Bind(options);
        return options;
    }

    [Fact]
    public void Defaults_KeepSeparateChoicesAndAllowSwitch()
    {
        var options = Bind(new());

        Assert.Equal(TestSolveChoiceDisplay.Separate, options.ChoiceDisplay);
        Assert.True(options.AllowStudentSwitch);
    }

    [Fact]
    public void EnvironmentValues_SelectInImageAndTurnSwitchOff()
    {
        var options = Bind(new()
        {
            ["TestSolve:ChoiceDisplay"] = "InImage",
            ["TestSolve:AllowStudentSwitch"] = "false",
        });

        Assert.Equal(TestSolveChoiceDisplay.InImage, options.ChoiceDisplay);
        Assert.False(options.AllowStudentSwitch);
    }

    [Fact]
    public void SettingsDto_SerializesChoiceDisplayAsName()
    {
        var json = JsonSerializer.Serialize(
            new TestSolveSettingsDto(TestSolveChoiceDisplay.InImage, true),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("{\"choiceDisplay\":\"InImage\",\"allowStudentSwitch\":true}", json);
    }
}
