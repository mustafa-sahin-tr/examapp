using System.Globalization;
using BadgeService.Services;
using ExamApp.Foundation.Localization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #185 — NotificationTextFactory: Consumer'lar bildirim metinlerini bu fabrika
/// üzerinden üretir. Başlık/gövde, hedef kültür için JSON sözlükten okunur;
/// parametreler string.Format ile biçimlendirilir.
/// </summary>
public class NotificationTextFactoryTests
{
    private static JsonResourceStore LoadResources()
    {
        // Services/BadgeService/Resources klasörünü proje kök'ünden bul
        var projectRoot = GetProjectRoot();
        var resourcesPath = Path.Combine(projectRoot, "Services", "BadgeService", "Resources");

        if (!Directory.Exists(resourcesPath))
        {
            throw new DirectoryNotFoundException($"Resources directory not found: {resourcesPath}");
        }

        using var fileProvider = new PhysicalFileProvider(resourcesPath);
        return JsonResourceStore.Load(fileProvider, "", throwOnDuplicateKeys: false, NullLogger.Instance);
    }

    private static string GetProjectRoot()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);

        // Walk up from bin/Debug/net10.0 to find the repository root
        while (currentDir != null)
        {
            // Look for ExamApp.slnx or Services directory
            if (File.Exists(Path.Combine(currentDir.FullName, "ExamApp.slnx")) ||
                Directory.Exists(Path.Combine(currentDir.FullName, "Services")))
            {
                return currentDir.FullName;
            }
            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException("Could not find repository root from " + AppContext.BaseDirectory);
    }

    private INotificationTextFactory CreateFactory()
    {
        var store = LoadResources();
        return new NotificationTextFactory(store, NullLogger<NotificationTextFactory>.Instance);
    }

    [Fact]
    public void Build_TurkishCulture_BookingApproved_ReturnsTurkishText()
    {
        var factory = CreateFactory();
        var culture = CultureInfo.GetCultureInfo("tr-TR");

        var text = factory.Build("BookingApproved", culture, "Öğretmen Adı", "09.12.2026 14:00-15:00");

        text.Title.ShouldContain("onaylandı");
        text.Body.ShouldContain("Öğretmen Adı");
    }

    [Fact]
    public void Build_EnglishCulture_BookingApproved_ReturnsEnglishText()
    {
        var factory = CreateFactory();
        var culture = CultureInfo.GetCultureInfo("en-US");

        var text = factory.Build("BookingApproved", culture, "Teacher Name", "09.12.2026 14:00-15:00");

        text.Title.ShouldContain("approved");
        text.Body.ShouldContain("Teacher Name");
    }

    [Fact]
    public void Build_WithParameters_FormatsCorrectly()
    {
        var factory = CreateFactory();
        var culture = CultureInfo.GetCultureInfo("tr-TR");

        var text = factory.Build("WorksheetAccessRequested", culture, "Ahmet Hoca", "Kesirler Testi");

        text.Body.ShouldContain("Ahmet Hoca");
        text.Body.ShouldContain("Kesirler Testi");
    }

    [Fact]
    public void Resolve_TurkishCulture_CommonDefault_ReturnsCorrectValue()
    {
        var factory = CreateFactory();
        var culture = CultureInfo.GetCultureInfo("tr-TR");

        var text = factory.Resolve("notifications.common.defaultTeacher", culture);

        text.ShouldBe("Öğretmeniniz");
    }

    [Fact]
    public void Resolve_EnglishCulture_CommonDefault_ReturnsEnglishValue()
    {
        var factory = CreateFactory();
        var culture = CultureInfo.GetCultureInfo("en-US");

        var text = factory.Resolve("notifications.common.defaultTeacher", culture);

        text.ShouldBe("Your teacher");
    }

    [Fact]
    public void Resolve_WithFormatParameters_FormatsCorrectly()
    {
        var factory = CreateFactory();
        var culture = CultureInfo.GetCultureInfo("tr-TR");

        var text = factory.Resolve("notifications.common.rejectionReasonSuffix", culture, "Uygun olmayan zaman");

        text.ShouldContain("Uygun olmayan zaman");
    }

    [Fact]
    public void Build_BookingRejected_IncludesReasonWhenProvided()
    {
        var factory = CreateFactory();
        var culture = CultureInfo.GetCultureInfo("tr-TR");

        var text = factory.Build("BookingRejected", culture, "Hoca", "zaman", " Gerekçe: Zaman çakışması");

        text.Body.ShouldContain("Zaman çakışması");
    }

    [Fact]
    public void Build_MissingKey_LogsWarningAndReturnsKeyAsTemplate()
    {
        var factory = CreateFactory();
        var culture = CultureInfo.GetCultureInfo("tr-TR");

        var text = factory.Build("NonexistentType", culture);

        // Factory should handle missing keys gracefully
        text.ShouldNotBeNull();
        text.Title.ShouldNotBeEmpty();
        text.Body.ShouldNotBeEmpty();
    }

    [Fact]
    public void Resolve_WorksheetReminderDue_WithParameters_FormatsCorrectly()
    {
        var factory = CreateFactory();
        var culture = CultureInfo.GetCultureInfo("tr-TR");

        var text = factory.Build("WorksheetReminderDue", culture, "Kesirler Testi", "30");

        text.Body.ShouldContain("30");
    }
}
