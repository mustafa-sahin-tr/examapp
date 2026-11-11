using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Tests.Models;

/// <summary>
/// Issue #1144: UI kaynağı adla gönderir (<c>"source":"Images"</c>); enum dönüştürücüsüz bu istek model bağlamada 400
/// alıyordu ve görsel aktarımı hiç başlamıyordu. ASP.NET Core'un varsayılan (Web) JSON ayarlarıyla çözülmeli.
/// </summary>
public class BeginBookImportUploadDtoJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("\"Pdf\"", BookImportSource.Pdf)]
    [InlineData("\"Images\"", BookImportSource.Images)]
    [InlineData("\"LooseImages\"", BookImportSource.LooseImages)]
    [InlineData("2", BookImportSource.Images)]
    public void Source_is_read_from_the_name_the_ui_sends(string source, BookImportSource expected)
    {
        var json = $$"""{"bookName":"Kitap","gradeId":22,"subjectId":null,"fileName":"sayfa-1.png","fileSizeBytes":1,"source":{{source}},"imageCount":6}""";

        var dto = JsonSerializer.Deserialize<BeginBookImportUploadDto>(json, Web)!;

        dto.Source.ShouldBe(expected);
        dto.ImageCount.ShouldBe(6);
    }
}
