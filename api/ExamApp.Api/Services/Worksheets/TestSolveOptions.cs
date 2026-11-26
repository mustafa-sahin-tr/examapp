using System.Text.Json.Serialization;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>Test çözme ekranında şıkların gösterimi.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestSolveChoiceDisplay
{
    /// <summary>Şıklar soru görselinin altında ayrı görsel düğmeler (eski düzen).</summary>
    Separate,

    /// <summary>Soru şıklarıyla tek görsel, altta harf düğmeleri; kalemle şıkkın üstü çizilebilir.</summary>
    InImage,
}

/// <summary>Test çözme ekranı ayarları (<c>TestSolve</c> bölümü); <c>GET api/worksheet/test-solve-settings</c> ile arayüze gider.</summary>
public class TestSolveOptions
{
    public const string Section = "TestSolve";

    /// <summary>Varsayılan şık gösterimi.</summary>
    public TestSolveChoiceDisplay ChoiceDisplay { get; set; } = TestSolveChoiceDisplay.Separate;

    /// <summary>Öğrenci iki gösterim arasında geçebilir mi (seçimi cihazda hatırlanır).</summary>
    public bool AllowStudentSwitch { get; set; } = true;
}

public sealed record TestSolveSettingsDto(TestSolveChoiceDisplay ChoiceDisplay, bool AllowStudentSwitch);
