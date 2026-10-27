using System.Text.Json.Serialization;

namespace ExamApp.Api.Models.Dtos;

/// <summary>
/// Issue #581: bir bölge içindeki dikdörtgen (sayfa pikseli, bölgenin sol üst köşesine göre). Şimdilik
/// <see cref="BulkQuestionDto.Masks"/> — basılı cevabı öğrenci görselinde örten maske. Doğrulama:
/// <see cref="ExamApp.Api.Helpers.QuestionImageMasks.IsValid"/> (negatif değil, kenarlar en az 1, sonlu sayı). İşaretleme manuel
/// kaydı (<see cref="Labeling.PredictionDto.Masks"/>) da taşır; ad sabit küçük harf (JSONB'de seçenekten bağımsız).
/// </summary>
public class RegionBoxDto
{
    [JsonPropertyName("x")]
    public double X { get; set; }
    [JsonPropertyName("y")]
    public double Y { get; set; }
    [JsonPropertyName("width")]
    public double Width { get; set; }
    [JsonPropertyName("height")]
    public double Height { get; set; }
}
