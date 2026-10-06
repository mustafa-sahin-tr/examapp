using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #365 (S1): soru aktarımı tamamen Admin-only. Her uç (sınıf + metot attribute'ları birlikte, AND semantiği)
/// yalnız Admin rolü isteyen bir attribute taşımalı ve <c>[AllowAnonymous]</c> olmamalı. İstisna listesi bilinçli olarak
/// boş; yeni bir uç eklenirse otomatik kapsanır (gerçek pipeline testi: QuestionTransferExportAccessEndpointsTests).
/// </summary>
public class QuestionTransferControllerAdminOnlyTests
{
    // Bilinçli istisna gerekirse action adı buraya yazılır (şu an yok).
    private static readonly string[] Exceptions = [];

    private static List<MethodInfo> Actions() =>
        typeof(QuestionTransferController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .ToList();

    private static IEnumerable<AuthorizeAttribute> AuthorizeAttributesOf(MethodInfo m) =>
        m.GetCustomAttributes<AuthorizeAttribute>().Concat(m.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>());

    [Fact]
    public void Controller_class_requires_only_the_admin_role()
    {
        typeof(QuestionTransferController).GetCustomAttributes<AuthorizeAttribute>()
            .Where(a => a.Roles is not null).Select(a => a.Roles!).ToArray()
            .ShouldBe(["Admin"]);
        typeof(QuestionTransferController).GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).ShouldBeEmpty();
    }

    [Fact]
    public void Every_endpoint_requires_the_admin_role()
    {
        var actions = Actions();
        // exports, imports, imports/preview, exports/sources, 6 export içerik GET'i, jobs, jobs/{id}
        actions.Count.ShouldBe(12);

        foreach (var m in actions.Where(m => !Exceptions.Contains(m.Name)))
        {
            m.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty(m.Name);
            AuthorizeAttributesOf(m).ShouldContain(a => a.Roles == "Admin", m.Name);
        }
    }

    [Fact]
    public void Hangfire_session_endpoints_moved_out_and_are_admin_or_superadmin_only()
    {
        typeof(QuestionTransferController).GetMethod("HangfireLogin").ShouldBeNull();
        typeof(QuestionTransferController).GetMethod("HangfireLogout").ShouldBeNull();

        var type = typeof(HangfireSessionController);
        foreach (var name in new[] { nameof(HangfireSessionController.HangfireLogin), nameof(HangfireSessionController.HangfireLogout) })
        {
            var attrs = type.GetMethod(name)!.GetCustomAttributes<AuthorizeAttribute>()
                .Concat(type.GetCustomAttributes<AuthorizeAttribute>()).ToList();
            attrs.Where(a => a.Roles is not null).Select(a => a.Roles!).ToArray().ShouldBe(["Admin,SuperAdmin"], name);
            attrs.ShouldNotContain(a => a.Policy == ApprovedTeacherPolicies.TeacherCapability, name);
        }
    }
}
