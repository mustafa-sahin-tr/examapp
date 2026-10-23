using System.Reflection;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #544: Excel ile toplu test yükleme kaldırıldı. <c>POST api/worksheet/bulk-import</c> ucu
/// (gateway: <c>/api/exam/worksheet/bulk-import</c>) geri gelmemeli.
/// </summary>
public class ExamControllerBulkImportRemovedTests
{
    [Fact]
    public void ExamController_HasNoBulkImportRoute()
    {
        var templates = typeof(ExamController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>())
            .Select(a => a.Template ?? string.Empty)
            .ToList();

        templates.ShouldNotBeEmpty();
        templates.ShouldNotContain(t => t.Contains("bulk-import", StringComparison.OrdinalIgnoreCase));
    }
}
