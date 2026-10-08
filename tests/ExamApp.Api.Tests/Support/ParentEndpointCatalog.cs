using System.Reflection;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace ExamApp.TestSupport;

/// <summary>Velinin okuyabildiği verinin kapsamı.</summary>
public enum ParentEndpointScope
{
    /// <summary>URL'de <c>studentId</c> taşıyan çocuk verisi: <c>IParentChildAccess</c> kapısı + <c>ParentAccessAudit</c>.</summary>
    Child,

    /// <summary>Velinin kendi listesi (çocuk id'si yok): kapsam token'daki veliyle sınırlı, audit yok.</summary>
    ParentOnly
}

/// <summary>Bir veli okuma ucu: aksiyon anahtarı (<c>Controller.Action</c>), kapsam, audit adı ve URL üreteci.</summary>
public sealed record ParentReadEndpoint(
    string Action,
    ParentEndpointScope Scope,
    string? AuditEndpoint,
    Func<int, int, string> Url)
{
    /// <summary>Çocuk id'si ve (test sonucu ucu için) o çocuğun bitmiş test oturumu id'si → istek yolu.</summary>
    public string For(int studentId, int testInstanceId) => Url(studentId, testInstanceId);

    public override string ToString() => Action;
}

/// <summary>
/// Issue #424 (epic #407 V6): veli uçlarının TEK kataloğu. <c>ParentAccessMatrixTests</c> (IDOR/yetki matrisi) ve
/// <c>ParentForbiddenContentTests</c> ("veli asla görmez" taraması) buradaki her uç için koşar.
/// <para>
/// Yeni bir veli ucu eklendiğinde <c>ParentAccessMatrixTests.Every_parent_endpoint_is_in_the_matrix</c> KIRILIR: uç ya
/// <see cref="ReadEndpoints"/>'e (matris + yasak içerik taraması otomatik koşar) ya da gerekçesiyle <see cref="Exempt"/>'e
/// eklenmelidir. Keşif yansıma ile: adı <c>Parent</c> ile başlayan controller'lar, rolü <c>Parent</c> içeren
/// <c>[Authorize]</c> taşıyan sınıf/aksiyonlar ve <c>api/parent</c> ile başlayan route'lar.
/// </para>
/// </summary>
public static class ParentEndpointCatalog
{
    public static readonly IReadOnlyList<ParentReadEndpoint> ReadEndpoints =
    [
        new($"{nameof(ParentDashboardController)}.{nameof(ParentDashboardController.GetChildSummary)}",
            ParentEndpointScope.Child, ParentAccessEndpoints.ChildSummary,
            (s, _) => $"/api/parent/children/{s}/summary"),
        new($"{nameof(ParentDashboardController)}.{nameof(ParentDashboardController.GetChildAssignments)}",
            ParentEndpointScope.Child, ParentAccessEndpoints.ChildAssignments,
            (s, _) => $"/api/parent/children/{s}/assignments"),
        new($"{nameof(ParentDashboardController)}.{nameof(ParentDashboardController.GetChildTestResult)}",
            ParentEndpointScope.Child, ParentAccessEndpoints.ChildTestResult,
            (s, t) => $"/api/parent/children/{s}/test-results/{t}"),
        new($"{nameof(ParentDashboardController)}.{nameof(ParentDashboardController.GetChildProgress)}",
            ParentEndpointScope.Child, ParentAccessEndpoints.ChildProgress,
            (s, _) => $"/api/parent/children/{s}/progress"),
        new($"{nameof(ParentDashboardController)}.{nameof(ParentDashboardController.GetChildSchedule)}",
            ParentEndpointScope.Child, ParentAccessEndpoints.ChildSchedule,
            (s, _) => $"/api/parent/children/{s}/schedule"),
        new($"{nameof(ParentLinksController)}.{nameof(ParentLinksController.GetMyChildren)}",
            ParentEndpointScope.ParentOnly, null,
            (_, _) => "/api/parent-links/my-children"),
    ];

    /// <summary>Veli kapsamında keşfedilen ama okuma matrisi dışında kalan aksiyonlar — her biri gerekçeli.</summary>
    public static readonly IReadOnlyDictionary<string, string> Exempt = new Dictionary<string, string>
    {
        [$"{nameof(ParentLinksController)}.{nameof(ParentLinksController.CreateSecondParentCode)}"] =
            "Parent write (#436): primary parent issues a second-parent code for their own Active link (other link 404, " +
            "non-primary 403); returns only the code + expiry; ParentLinkEndpointsTests, ParentForbiddenContentTests.",
        [$"{nameof(ParentLinksController)}.{nameof(ParentLinksController.GetMyParents)}"] =
            "Student-only read of the student's own parents; ParentLinkEndpointsTests.",
        [$"{nameof(ParentLinksController)}.{nameof(ParentLinksController.Redeem)}"] =
            "Parent write; returns only a Pending stub without student data; ParentLinkEndpointsTests.",
        [$"{nameof(ParentLinksController)}.{nameof(ParentLinksController.Approve)}"] =
            "Write (Student,Parent; #436): primary parent approves a pending second-parent request (unrelated 404, non-primary " +
            "403); student only a LegacyV1 request in the 30-day transition. 204 without body; ParentLinkEndpointsTests.",
        [$"{nameof(ParentLinksController)}.{nameof(ParentLinksController.Reject)}"] =
            "Write (Student,Parent; #436): same authorization as Approve; 204 without body; ParentLinkEndpointsTests.",
        [$"{nameof(ParentLinksController)}.{nameof(ParentLinksController.Revoke)}"] =
            "Write (Parent,Admin; #436): primary parent removes any link of the child, a parent leaves / cancels their own " +
            "(last Active parent 409), admin any (audited); unrelated 404. Exercised by the revoke step of the matrix.",
        [$"{nameof(ParentController)}.{nameof(ParentController.RegisterParent)}"] = "Role registration; no child data.",
        [$"{nameof(ParentController)}.{nameof(ParentController.CheckParent)}"] = "Own profile flag only; no child data.",
    };

    /// <summary>
    /// Ters keşif (issue #424 review): Parent rolüyle yetki politikası GEÇEN (ya da anonim) ama veli çocuk verisi okuma ucu
    /// OLMAYAN uçlar — her biri gerekçeli. Anahtar: controller aksiyonu için <c>Controller.Action</c>, diğer uçlar için
    /// <c>route:/şablon</c>. Yeni bir uç veliye açılırsa ve burada / <see cref="ReadEndpoints"/> / <see cref="Exempt"/>'te yoksa
    /// <c>ParentAccessMatrixTests.Every_endpoint_a_parent_can_reach_is_in_the_matrix_or_allowlisted</c> kırılır.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ParentReachableAllowlist = new Dictionary<string, string>
    {
        ["AuthController.GetCurrentCulture"] =
            "Own session: active UI culture.",
        ["AuthController.Logout"] =
            "Own session: logout.",
        ["AuthController.RefreshProfileInformation"] =
            "Own profile refresh (caller's own user only).",
        ["AuthController.RefreshToken"] =
            "Own session: token refresh.",
        ["BookingController.GetTeacherSlots"] =
            "Public calendar of independent tutors (open, unbooked slots; no student data) (#418).",
        ["BooksController.GetBookTestsByBookId"] =
            "Reference data (book catalogue).",
        ["BooksController.GetBooksAsync"] =
            "Reference data (book catalogue).",
        ["ExamController.GetGrades"] =
            "Reference data (grades).",
        ["ProgramController.GetProgramSteps"] =
            "Anonymous reference data (program steps).",
        ["SchoolController.GetSchools"] =
            "Reference data (school list).",
        ["StudentController.CheckStudent"] =
            "Self-scoped: does the CALLER have a Student record (parent: false).",
        ["StudentController.GetGradesAsync"] =
            "Reference data (grades).",
        ["StudentController.GetStudentProfile"] =
            "Self-scoped: the CALLER's own student profile by token user id (parent: 404); no student id parameter.",
        ["StudentController.RegisterStudent"] =
            "Self registration; a parent account is rejected with 409 (#419).",
        ["StudentController.UpdateTheme"] =
            "Self-scoped: the CALLER's own student theme (parent has no Student record).",
        ["SubjectController.GetSubTopicsByTopic"] =
            "Reference data (taxonomy).",
        ["SubjectController.GetSubjectAsync"] =
            "Reference data (taxonomy).",
        ["SubjectController.GetSubjectsByGrade"] =
            "Reference data (taxonomy).",
        ["SubjectController.GetTopicsBySubject"] =
            "Reference data (taxonomy).",
        ["SubjectController.GetTopicsBySubjectAndGrade"] =
            "Reference data (taxonomy).",
        ["TeacherController.CheckTeacher"] =
            "Self-scoped: does the CALLER have a Teacher record (parent: false).",
        ["TeacherController.RegisterTeacher"] =
            "Self registration; a parent account is rejected with 409 (#419).",
        ["TeacherController.UpdateTheme"] =
            "Self-scoped theme; UI sends non-students here (parent has no Teacher record → no-op/404).",
    };

    /// <summary>Veliye KAPALI olması gereken uçlar (ters keşif bunları ayrıca doğrular).</summary>
    public static readonly IReadOnlyList<string> MustDenyParent =
    [
        // Başka öğrencilerin ad/puanı; veli çocuğunun sırasını V4 progress ile görür.
        "LeaderboardController.GetLeaderboard",
    ];

    public static ParentReadEndpoint Get(string action) => ReadEndpoints.Single(e => e.Action == action);

    /// <summary>Yansımayla veli kapsamındaki tüm controller aksiyonları (<c>Controller.Action</c>).</summary>
    public static IReadOnlyList<(string Key, MethodInfo Method, Type Controller)> DiscoverParentActions()
    {
        var result = new List<(string, MethodInfo, Type)>();
        var controllers = typeof(ParentDashboardController).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

        foreach (var controller in controllers)
        {
            var controllerIsParent = controller.Name.StartsWith("Parent", StringComparison.Ordinal)
                || HasParentRole(controller)
                || controller.GetCustomAttributes<RouteAttribute>().Any(r => IsParentRoute(r.Template));

            var actions = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any());
            foreach (var action in actions)
            {
                var actionIsParent = controllerIsParent
                    || HasParentRole(action)
                    || action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any(h => IsParentRoute(h.Template));
                if (actionIsParent)
                    result.Add(($"{controller.Name}.{action.Name}", action, controller));
            }
        }

        return result;
    }

    private static bool HasParentRole(MemberInfo member)
        => member.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any(a =>
            (a.Roles ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains("Parent", StringComparer.Ordinal));

    private static bool IsParentRoute(string? template)
        => template != null && template.TrimStart('/').StartsWith("api/parent", StringComparison.OrdinalIgnoreCase);
}
