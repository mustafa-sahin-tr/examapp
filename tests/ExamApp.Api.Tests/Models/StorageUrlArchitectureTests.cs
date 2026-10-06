using System.Reflection;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Storage;

namespace ExamApp.Api.Tests.Models;

/// <summary>
/// issue #365 (S2) mimari kural: DTO'lardaki her <c>*Url</c> string alanı ya <see cref="StorageUrlAttribute"/> (MVC
/// çıktısında imzalanır) ya da gerekçeli <see cref="NotStorageUrlAttribute"/> taşır. S4'te bucket'lar özel olunca
/// imzasız kalan bir görsel alanı sessizce kırılır; bu test yeni alanın karar verilmeden eklenmesini engeller.
/// </summary>
public class StorageUrlArchitectureTests
{
    private static readonly Assembly Api = typeof(QuestionTransferJobDto).Assembly;

    private static IEnumerable<Type> DtoTypes() => Api.GetTypes().Where(t =>
        t is { IsClass: true } &&
        !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)) &&
        (t.Name.EndsWith("Dto", StringComparison.Ordinal) ||
         (t.Namespace?.StartsWith("ExamApp.Api.Models", StringComparison.Ordinal) ?? false)));

    private static IEnumerable<PropertyInfo> UrlProperties(Type t) => t
        .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(p => p.PropertyType == typeof(string) &&
                    (p.Name.EndsWith("Url", StringComparison.OrdinalIgnoreCase) ||
                     p.Name.EndsWith("Uri", StringComparison.OrdinalIgnoreCase)));

    [Fact]
    public void Scan_finds_the_known_dto_url_fields()
    {
        // Tarama gerçekten bir şey buluyor mu (boş küme ile yeşil geçmesin).
        var all = DtoTypes().SelectMany(UrlProperties).Select(p => $"{p.DeclaringType!.Name}.{p.Name}").ToList();
        all.ShouldContain("QuestionDto.ImageUrl");
        all.ShouldContain("StudyItemImageDto.ImageUrl");
        all.ShouldContain("QuestionTransferJobDto.FileUrl");
        all.Count.ShouldBeGreaterThan(20);
    }

    [Fact]
    public void Every_dto_url_field_is_signed_or_explicitly_exempted()
    {
        var undecided = DtoTypes().SelectMany(UrlProperties)
            .Where(p => !p.IsDefined(typeof(StorageUrlAttribute), true) && !p.IsDefined(typeof(NotStorageUrlAttribute), true))
            .Select(p => $"{p.DeclaringType!.FullName}.{p.Name}")
            .ToList();

        undecided.ShouldBeEmpty(
            "Add [StorageUrl(StorageArea.X)] (MinIO image shown in the browser) or [NotStorageUrl(\"reason\")]: " +
            string.Join(", ", undecided));
    }

    [Fact]
    public void No_field_is_both_signed_and_exempted_and_attributes_sit_only_on_strings()
    {
        var props = Api.GetTypes()
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        foreach (var p in props)
        {
            var signed = p.IsDefined(typeof(StorageUrlAttribute), false);
            var exempt = p.IsDefined(typeof(NotStorageUrlAttribute), false);
            (signed && exempt).ShouldBeFalse($"{p.DeclaringType!.FullName}.{p.Name}");
            if (signed)
                p.PropertyType.ShouldBe(typeof(string), $"{p.DeclaringType!.FullName}.{p.Name}");
        }
    }

    [Theory]
    [InlineData(typeof(QuestionTransferJobDto))]
    [InlineData(typeof(QuestionTransferExportBundleDto))]
    public void Question_transfer_file_urls_are_explicitly_never_signed(Type type)
    {
        var p = type.GetProperty("FileUrl")!;
        p.IsDefined(typeof(StorageUrlAttribute), true).ShouldBeFalse();
        p.IsDefined(typeof(NotStorageUrlAttribute), true).ShouldBeTrue();
    }

    [Theory]
    [InlineData(typeof(QuestionDto), "ImageUrl", StorageArea.QuestionImage)]
    [InlineData(typeof(QuestionDto), "ImageUrlV2", StorageArea.QuestionImage)]
    [InlineData(typeof(PassageDto), "ImageUrl", StorageArea.QuestionImage)]
    [InlineData(typeof(AnswerDto), "ImageUrl", StorageArea.QuestionImage)]
    [InlineData(typeof(WorksheetSampleQuestionDto), "ImageUrl", StorageArea.QuestionImage)]
    [InlineData(typeof(WorksheetDto), "ImageUrl", StorageArea.WorksheetCover)]
    [InlineData(typeof(AssignedWorksheetDto), "ImageUrl", StorageArea.WorksheetCover)]
    [InlineData(typeof(InstanceSummaryDto), "ImageUrl", StorageArea.WorksheetCover)]
    [InlineData(typeof(StudyItemDto), "CoverImageUrl", StorageArea.StudyPage)]
    [InlineData(typeof(StudyItemImageDto), "ImageUrl", StorageArea.StudyPage)]
    [InlineData(typeof(StudyPageAttachImageResponseDto), "ImageUrl", StorageArea.StudyPage)]
    public void Key_image_fields_carry_the_expected_area(Type type, string property, StorageArea area)
    {
        var attr = type.GetProperty(property)!.GetCustomAttribute<StorageUrlAttribute>(true);
        attr.ShouldNotBeNull();
        attr.Areas.ShouldContain(area);
    }
}
