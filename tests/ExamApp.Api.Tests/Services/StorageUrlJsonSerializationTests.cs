using System.Text.Json;
using System.Text.Json.Nodes;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Storage;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #365 (S2): <see cref="StorageUrlJsonOptionsSetup"/> yalnız MVC JSON çıktısını imzalar. SignalR hub yükleri,
/// minimal API JSON'u ve varsayılan <see cref="JsonSerializer"/> (Redis önbelleği — UserProfileCacheService, outbox)
/// imzasız kalır; böylece imzalı URL hiçbir yerde saklanmaz/önbelleğe girmez. Okuma yolu ve bellekteki DTO değişmez.
/// </summary>
public class StorageUrlJsonSerializationTests
{
    /// <summary>Hangi değer, hangi alanlarla imzalatıldı — kaydeden sahte imzalayıcı.</summary>
    private sealed class RecordingSigner : IStorageUrlSigner
    {
        public List<(string? Value, string Areas)> Calls { get; } = [];

        public string? SignForBrowser(string? storedUrl, IReadOnlyList<StorageArea> areas)
        {
            Calls.Add((storedUrl, string.Join(",", areas)));
            return storedUrl is null ? null : storedUrl + "?signed";
        }
    }

    private static QuestionDto SampleQuestion() => new()
    {
        Id = 1,
        Text = "q",
        CategoryName = "c",
        ImageUrl = "/img/exam-questions/questions/1/q.jpg",
        Passage = new PassageDto { Id = 2, ImageUrl = "/img/exam-questions/passages/p.jpg" },
        Answers = [new AnswerDto { Id = 3, ImageUrl = "/img/exam-questions/answers/1/a.jpg" }, new AnswerDto { Id = 4 }],
    };

    private static ServiceProvider BuildProvider(IStorageUrlSigner signer)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles); // Program.cs ile aynı
        services.AddSignalR();
        services.AddSingleton(signer);
        services.AddSingleton<IConfigureOptions<MvcJsonOptions>, StorageUrlJsonOptionsSetup>(); // Program.cs ile aynı
        return services.BuildServiceProvider();
    }

    private static JsonSerializerOptions MvcOptions(IServiceProvider sp) =>
        sp.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions;

    [Fact]
    public void Mvc_output_signs_every_attributed_field_including_nested_ones_with_their_areas()
    {
        var signer = new RecordingSigner();
        using var sp = BuildProvider(signer);

        var node = JsonNode.Parse(JsonSerializer.Serialize(SampleQuestion(), MvcOptions(sp)))!;

        node["imageUrl"]!.GetValue<string>().ShouldBe("/img/exam-questions/questions/1/q.jpg?signed");
        node["imageUrlV2"].ShouldBeNull(); // q.jpg → v2 kardeşi yok
        node["passage"]!["imageUrl"]!.GetValue<string>().ShouldBe("/img/exam-questions/passages/p.jpg?signed");
        node["answers"]![0]!["imageUrl"]!.GetValue<string>().ShouldBe("/img/exam-questions/answers/1/a.jpg?signed");
        node["answers"]![1]!["imageUrl"].ShouldBeNull(); // null → null
        signer.Calls.ShouldAllBe(c => c.Areas == nameof(StorageArea.QuestionImage));
    }

    [Fact]
    public void Canvas_v2_variant_is_derived_server_side_and_signed_separately()
    {
        var signer = new RecordingSigner();
        using var sp = BuildProvider(signer);
        var dto = new QuestionDto { Text = "q", CategoryName = "c", ImageUrl = "/img/exam-questions/questions/g/question.jpg" };

        var node = JsonNode.Parse(JsonSerializer.Serialize(dto, MvcOptions(sp)))!;

        node["imageUrl"]!.GetValue<string>().ShouldBe("/img/exam-questions/questions/g/question.jpg?signed");
        node["imageUrlV2"]!.GetValue<string>().ShouldBe("/img/exam-questions/questions/g/question-v2.jpg?signed");
    }

    [Fact]
    public void Dto_held_in_an_object_typed_wrapper_is_still_signed()
    {
        var signer = new RecordingSigner();
        using var sp = BuildProvider(signer);

        object wrapper = new { data = new WorksheetDto { Id = 5, ImageUrl = "/img/worksheets/5-background.png" } };
        var json = JsonSerializer.Serialize(wrapper, MvcOptions(sp));

        json.ShouldContain("/img/worksheets/5-background.png?signed");
        signer.Calls.ShouldContain(c => c.Areas == nameof(StorageArea.WorksheetCover));
    }

    [Fact]
    public void Question_transfer_file_url_is_never_passed_to_the_signer()
    {
        var signer = new RecordingSigner();
        using var sp = BuildProvider(signer);
        var job = new QuestionTransferJobDto { FileUrl = "/img/exam-questions/question-transfer/exports/default/bundle-0001.zip" };
        var bundle = new QuestionTransferExportBundleDto { FileUrl = "/img/exam-questions/question-transfer/exports/default/index.json" };

        var json = JsonSerializer.Serialize(job, MvcOptions(sp)) + JsonSerializer.Serialize(bundle, MvcOptions(sp));

        json.ShouldNotContain("?signed");
        signer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Serialization_does_not_mutate_the_in_memory_dto_and_reading_is_unaffected()
    {
        var signer = new RecordingSigner();
        using var sp = BuildProvider(signer);
        var dto = SampleQuestion();

        _ = JsonSerializer.Serialize(dto, MvcOptions(sp));
        dto.ImageUrl.ShouldBe("/img/exam-questions/questions/1/q.jpg");

        signer.Calls.Clear();
        var read = JsonSerializer.Deserialize<QuestionDto>(
            """{"imageUrl":"/img/exam-questions/questions/1/q.jpg?X-Amz-Signature=abc","passage":{"imageUrl":"/img/x"}}""",
            MvcOptions(sp))!;
        read.ImageUrl.ShouldBe("/img/exam-questions/questions/1/q.jpg?X-Amz-Signature=abc"); // normalizasyon servis işi
        read.Passage!.ImageUrl.ShouldBe("/img/x");
        signer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void SignalR_minimal_api_and_default_serializers_are_not_signed()
    {
        var signer = new RecordingSigner();
        using var sp = BuildProvider(signer);
        var dto = SampleQuestion();

        var hub = sp.GetRequiredService<IOptions<JsonHubProtocolOptions>>().Value.PayloadSerializerOptions;
        var minimal = sp.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;

        foreach (var options in new[] { hub, minimal, new JsonSerializerOptions(), new JsonSerializerOptions(JsonSerializerDefaults.Web) })
            JsonSerializer.Serialize(dto, options).ShouldNotContain("?signed");
        JsonSerializer.Serialize(dto).ShouldNotContain("?signed"); // UserProfileCacheService / outbox deseni

        signer.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Existing_mvc_json_settings_are_preserved()
    {
        using var sp = BuildProvider(new RecordingSigner());
        var options = MvcOptions(sp);

        options.ReferenceHandler.ShouldBe(System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles);
        options.PropertyNamingPolicy.ShouldBe(JsonNamingPolicy.CamelCase);
    }
}
