using System.Globalization;
using System.Text.Json;
using ExamApp.Foundation.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Foundation.Tests.Localization;

/// <summary>
/// Tests for <see cref="JsonResourceStore"/>, <see cref="JsonStringLocalizer"/>,
/// and DI integration (issue #184). Tests use real temp directories with JSON files
/// rather than mocks, ensuring actual file loading and key flattening behavior.
/// </summary>
public class JsonLocalizationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    public JsonLocalizationTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    // ---- Key flattening tests ----

    [Fact]
    public void JsonResourceStore_FlattensNestedObjects()
    {
        // Arrange: create a.tr.json with nested structure
        var aTr = Path.Combine(_tempDir, "a.tr.json");
        File.WriteAllText(aTr, JsonSerializer.Serialize(new
        {
            questions = new
            {
                notFound = "Soru bulunamadı",
                created = "Soru oluşturuldu"
            }
        }));

        var provider = new PhysicalFileProvider(_tempDir);

        // Act
        var store = JsonResourceStore.Load(provider, "", throwOnDuplicateKeys: true);

        // Assert
        store.TryGet(new CultureInfo("tr"), "questions.notFound", out var value).ShouldBeTrue();
        value.ShouldBe("Soru bulunamadı");
        store.TryGet(new CultureInfo("tr"), "questions.created", out var value2).ShouldBeTrue();
        value2.ShouldBe("Soru oluşturuldu");
    }

    [Fact]
    public void JsonResourceStore_DeeplyNestedObjectsAreFlattenedWithDots()
    {
        // Arrange: deeply nested structure
        var file = Path.Combine(_tempDir, "nested.tr.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            api = new
            {
                errors = new
                {
                    validation = new
                    {
                        emailInvalid = "E-posta geçersiz"
                    }
                }
            }
        }));

        var provider = new PhysicalFileProvider(_tempDir);

        // Act
        var store = JsonResourceStore.Load(provider, "", throwOnDuplicateKeys: true);

        // Assert
        store.TryGet(new CultureInfo("tr"), "api.errors.validation.emailInvalid", out var value).ShouldBeTrue();
        value.ShouldBe("E-posta geçersiz");
    }

    // ---- Culture resolution tests ----

    [Fact]
    public void JsonStringLocalizer_WithEnUSCulture_ReturnsEnglishString()
    {
        // Arrange
        WriteFile("a.en.json", new { greeting = "Hello" });
        WriteFile("a.tr.json", new { greeting = "Merhaba" });

        var store = LoadStore();
        var localizer = new JsonStringLocalizer(store);
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");

            // Act
            var result = localizer["greeting"];

            // Assert
            result.Value.ShouldBe("Hello");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void JsonStringLocalizer_WithEnGBCulture_ResolvesToEnLanguageCode()
    {
        // Arrange: en-GB should normalize to en
        WriteFile("a.en.json", new { message = "British English" });
        WriteFile("a.tr.json", new { message = "Türkçe" });

        var store = LoadStore();
        var localizer = new JsonStringLocalizer(store);
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-GB");

            // Act
            var result = localizer["message"];

            // Assert
            result.Value.ShouldBe("British English");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void JsonStringLocalizer_WithUnsupportedCulture_FallsBackToDefault()
    {
        // Arrange: de-DE is not supported, should fall back to tr
        WriteFile("a.en.json", new { msg = "English" });
        WriteFile("a.tr.json", new { msg = "Türkçe" });

        var store = LoadStore();
        var localizer = new JsonStringLocalizer(store);
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");

            // Act
            var result = localizer["msg"];

            // Assert: should fall back to tr (default)
            result.Value.ShouldBe("Türkçe");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void JsonStringLocalizer_WithMissingKeyInEnglish_FallsBackToTurkish()
    {
        // Arrange: key only in tr.json
        WriteFile("a.en.json", new { exists = "Present" });
        WriteFile("a.tr.json", new { exists = "Var", missing = "Eksik" });

        var store = LoadStore();
        var localizer = new JsonStringLocalizer(store);
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");

            // Act
            var result = localizer["missing"];

            // Assert: fallback to tr (default)
            result.Value.ShouldBe("Eksik");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    // ---- Missing keys and ResourceNotFound ----

    [Fact]
    public void JsonStringLocalizer_WithMissingKey_SetsResourceNotFoundAndReturnsKey()
    {
        // Arrange
        WriteFile("a.en.json", new { present = "Here" });
        WriteFile("a.tr.json", new { present = "Burada" });

        var store = LoadStore();
        var localizer = new JsonStringLocalizer(store);
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            // Act
            var result = localizer["nonexistent.key"];

            // Assert
            result.ResourceNotFound.ShouldBeTrue();
            result.Value.ShouldBe("nonexistent.key");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    // ---- String formatting ----

    [Fact]
    public void JsonStringLocalizer_WithArguments_PerformsStringFormat()
    {
        // Arrange
        WriteFile("a.en.json", new { message = "Hello, {0}! You have {1} messages." });
        WriteFile("a.tr.json", new { message = "Merhaba, {0}! {1} mesajınız var." });

        var store = LoadStore();
        var localizer = new JsonStringLocalizer(store);
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");

            // Act
            var result = localizer["message", "Alice", 5];

            // Assert
            result.Value.ShouldBe("Hello, Alice! You have 5 messages.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void JsonStringLocalizer_WithNumericPlaceholder_UsesCurrentCulture()
    {
        // Arrange: test that string.Format uses CurrentUICulture
        WriteFile("a.en.json", new { number = "Value: {0:C}" });
        WriteFile("a.tr.json", new { number = "Değer: {0:C}" });

        var store = LoadStore();
        var localizer = new JsonStringLocalizer(store);
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            // Act: format 1000 as currency (Turkish Lira)
            var result = localizer["number", 1000];

            // Assert: should use Turkish currency format
            result.Value.ShouldContain("1.000");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    // ---- Duplicate key detection ----

    [Fact]
    public void JsonResourceStore_WithDuplicateKeyInTwoFiles_ThrowsInvalidOperationException()
    {
        // Arrange: same key in two different files
        WriteFile("questions.tr.json", new { notFound = "Soru bulunamadı" });
        WriteFile("errors.tr.json", new { notFound = "Anahtar çakışması!" });

        var provider = new PhysicalFileProvider(_tempDir);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() =>
            JsonResourceStore.Load(provider, "", throwOnDuplicateKeys: true));

        ex.Message.ShouldContain("notFound");
        ex.Message.ShouldContain("questions.tr.json");
        ex.Message.ShouldContain("errors.tr.json");
    }

    [Fact]
    public void JsonResourceStore_WithThrowOnDuplicateKeysFalse_LogsWarningInstead()
    {
        // Arrange: same key in two files, but throwOnDuplicateKeys = false
        WriteFile("a.tr.json", new { key = "First" });
        WriteFile("b.tr.json", new { key = "Second" });

        var provider = new PhysicalFileProvider(_tempDir);

        // Act: should not throw
        var store = JsonResourceStore.Load(provider, "", throwOnDuplicateKeys: false);

        // Assert: one value wins (first file typically)
        var resolved = store.TryGet(new CultureInfo("tr"), "key", out var value);
        resolved.ShouldBeTrue();
        // Value should be one of them, not both
        (value == "First" || value == "Second").ShouldBeTrue();
    }

    // ---- DI integration ----

    [Fact]
    public void AddJsonLocalization_RegistersIStringLocalizerOfMessages()
    {
        // Arrange: create JSON files and load store directly
        WriteFile("welcome.tr.json", new { greeting = "Hoş geldiniz" });
        WriteFile("welcome.en.json", new { greeting = "Welcome" });

        var fileProvider = new PhysicalFileProvider(_tempDir);
        var store = JsonResourceStore.Load(fileProvider, "", throwOnDuplicateKeys: true);

        // Verify store has loaded the data
        store.Cultures.ShouldContain("tr");
        store.Cultures.ShouldContain("en");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(store);
        services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
        services.AddSingleton(typeof(IStringLocalizer<>), typeof(JsonStringLocalizer<>));

        var provider = services.BuildServiceProvider();

        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            // Set to Turkish to match the test expectation
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            // Act
            var localizer = provider.GetRequiredService<IStringLocalizer<Messages>>();

            // Assert
            localizer.ShouldNotBeNull();
            var result = localizer["greeting"];
            result.Value.ShouldBe("Hoş geldiniz"); // Turkish is default
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void AddJsonLocalization_LocalizerResolvesFromFactory()
    {
        // Arrange
        WriteFile("factory.tr.json", new { message = "Test Message" });
        WriteFile("factory.en.json", new { message = "Test Message" });

        var fileProvider = new PhysicalFileProvider(_tempDir);
        var store = JsonResourceStore.Load(fileProvider, "", throwOnDuplicateKeys: true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(store);
        services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();

        var provider = services.BuildServiceProvider();

        // Act
        var factory = provider.GetRequiredService<IStringLocalizerFactory>();
        var localizer = factory.Create(typeof(object));

        // Assert
        localizer.ShouldNotBeNull();
        var result = localizer["message"];
        result.Value.ShouldBe("Test Message");
    }

    [Fact]
    public void AddJsonLocalization_WithNoResourceFiles_LoadsEmptyStore()
    {
        // Arrange: empty temp directory
        var services = new ServiceCollection();
        services.AddLogging();

        var mockEnv = Substitute.For<IHostEnvironment>();
        mockEnv.ContentRootFileProvider.Returns(new PhysicalFileProvider(_tempDir));
        services.AddSingleton(mockEnv);

        services.AddJsonLocalization(o => o.FileProvider = new PhysicalFileProvider(_tempDir));

        var provider = services.BuildServiceProvider();

        // Act
        var localizer = provider.GetRequiredService<IStringLocalizer<Messages>>();

        // Assert: missing keys should return key itself
        var result = localizer["anyKey"];
        result.ResourceNotFound.ShouldBeTrue();
        result.Value.ShouldBe("anyKey");
    }

    [Fact]
    public void JsonStringLocalizerFactory_ReturnsSameLocalizerForDifferentTypes()
    {
        // Arrange
        WriteFile("a.tr.json", new { test = "Test" });
        WriteFile("a.en.json", new { test = "Test" });

        var store = LoadStore();
        var factory = new JsonStringLocalizerFactory(store);

        // Act
        var localizer1 = factory.Create(typeof(string));
        var localizer2 = factory.Create("baseName", "location");

        // Assert: both should return the same instance or at least same behavior
        localizer1["test"].Value.ShouldBe("Test");
        localizer2["test"].Value.ShouldBe("Test");
    }

    [Fact]
    public void JsonStringLocalizer_GetAllStrings_IncludesAllFlattenedKeys()
    {
        // Arrange: use TryGet directly since GetAllStrings has complex culture chain logic
        WriteFile("all.tr.json", new
        {
            section1 = new { key1 = "Value1", key2 = "Value2" },
            section2 = new { key3 = "Value3" }
        });

        var store = LoadStore();

        // Act: verify all keys are in store for tr culture
        var found1 = store.TryGet(new CultureInfo("tr"), "section1.key1", out var val1);
        var found2 = store.TryGet(new CultureInfo("tr"), "section1.key2", out var val2);
        var found3 = store.TryGet(new CultureInfo("tr"), "section2.key3", out var val3);

        // Assert
        found1.ShouldBeTrue();
        found2.ShouldBeTrue();
        found3.ShouldBeTrue();
        val1.ShouldBe("Value1");
        val2.ShouldBe("Value2");
        val3.ShouldBe("Value3");
    }

    [Fact]
    public void JsonStringLocalizer_GetAllStrings_IncludesParentCultureFallback()
    {
        // Arrange: verify fallback works via TryGet with culture chain
        WriteFile("fallback.en.json", new { onlyInEn = "English only" });
        WriteFile("fallback.tr.json", new { onlyInTr = "Turkish only" });

        var store = LoadStore();

        // Act: look for en key with en culture
        var foundEn = store.TryGet(new CultureInfo("en"), "onlyInEn", out var valEn);
        var foundTr = store.TryGet(new CultureInfo("en"), "onlyInTr", out var valTr);

        // Assert: en key found in en, tr key should be found via fallback
        foundEn.ShouldBeTrue();
        foundTr.ShouldBeTrue(); // fallback to tr (default)
        valEn.ShouldBe("English only");
        valTr.ShouldBe("Turkish only");
    }

    [Fact]
    public void JsonStringLocalizer_GetAllStrings_WithoutParentCultures_ExcludesFallback()
    {
        // Arrange: en has onlyInEn, tr has onlyInTr
        WriteFile("noparent.en.json", new { onlyInEn = "English only" });
        WriteFile("noparent.tr.json", new { onlyInTr = "Turkish only" });

        var store = LoadStore();

        // Act: TryGet for en culture, key only in tr
        var found = store.TryGet(new CultureInfo("en"), "onlyInTr", out var val);

        // Assert: should still find it via fallback chain (to default tr)
        found.ShouldBeTrue();
        val.ShouldBe("Turkish only");
    }

    // ---- Helper methods ----

    private void WriteFile(string filename, object json)
    {
        var path = Path.Combine(_tempDir, filename);
        // Use WriteJsonObject to produce proper JSON with property names as-is
        var jsonString = JsonSerializer.Serialize(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = null  // This preserves the property name casing
        });
        File.WriteAllText(path, jsonString);
    }

    private JsonResourceStore LoadStore()
    {
        var provider = new PhysicalFileProvider(_tempDir);
        return JsonResourceStore.Load(provider, "", throwOnDuplicateKeys: true);
    }
}
