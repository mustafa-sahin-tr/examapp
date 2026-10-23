using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExamApp.Foundation.Tests.Repo;

/// <summary>
/// issue #333 — UI Content-Security-Policy'si üç yerde tanımlı: <c>ui/angular.json</c> ve <c>auth-ui/angular.json</c> dev-server
/// başlığı (yerel / Aspire, gateway üzerinden) ve prod nginx şablonu (<c>deploy/nginx/angular-spa.conf.template</c>). Bu testler
/// üçünün aynı direktifleri taşıdığını ve politikayı kıracak satır içi betik eklenmediğini denetler.
/// </summary>
public class UiContentSecurityPolicyTests
{
    private const string HeaderName = "Content-Security-Policy-Report-Only";
    private const string DevJitsiOrigin = "http://localhost:8000";

    /// <summary>
    /// Yalnız dev-server politikasında: Vite HMR istemcisi doğrudan dev-server portuna websocket açar (gateway'den geçmez).
    /// Prod (nginx) politikasında yoktur.
    /// </summary>
    private const string DevHmrSource = "ws://localhost:*";

    private static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "rabbitmq", "definitions.json")))
            dir = dir.Parent;
        dir.ShouldNotBeNull("repo kökü bulunamadı");
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath));
    }

    private static string DevServerPolicy(string angularJsonPath)
    {
        using var doc = JsonDocument.Parse(RepoFile(angularJsonPath));
        var project = doc.RootElement.GetProperty("projects").EnumerateObject().First().Value;
        return project.GetProperty("architect").GetProperty("serve").GetProperty("options")
            .GetProperty("headers").GetProperty(HeaderName).GetString()!;
    }

    private static string NginxTemplate() => RepoFile("deploy/nginx/angular-spa.conf.template");

    /// <summary>
    /// nginx'te bir location kendi add_header'ını tanımlarsa server seviyesindekiler devralınmaz → başlık bloğu HER location'da
    /// tekrarlanır. Tüm kopyalar aynı olmalı; tek politika döner.
    /// </summary>
    private static int LocationCount()
        => Regex.Matches(NginxTemplate(), @"^\s*location\s", RegexOptions.Multiline).Count;

    private static string NginxPolicy()
    {
        var policies = Regex.Matches(NginxTemplate(), $"add_header {Regex.Escape(HeaderName)} \"([^\"]+)\" always;")
            .Select(m => m.Groups[1].Value).ToList();
        policies.Count.ShouldBe(LocationCount(),
            "her location CSP başlığını taşımalı");
        policies.Distinct().Count().ShouldBe(1, "location'lardaki politikalar farklı");
        return policies[0];
    }

    /// <summary>Direktif adı → kaynak listesi (sıra bağımsız).</summary>
    private static Dictionary<string, string[]> Parse(string policy) => policy
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(d => d.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToDictionary(p => p[0], p => p.Skip(1).Order(StringComparer.Ordinal).ToArray());

    [Fact]
    public void Ui_dev_server_and_nginx_template_share_the_same_policy()
    {
        var ui = Parse(DevServerPolicy("ui/angular.json"));
        var nginx = Parse(NginxPolicy().Replace("${CSP_JITSI_ORIGIN}", DevJitsiOrigin));

        ui["connect-src"].ShouldContain(DevHmrSource);
        nginx["connect-src"].ShouldNotContain(DevHmrSource);
        nginx.Keys.Order().ShouldBe(ui.Keys.Order());
        foreach (var (directive, sources) in ui)
            nginx[directive].ShouldBe(sources.Where(s => s != DevHmrSource).ToArray(), $"{directive} farklı");
    }

    [Fact]
    public void Auth_ui_policy_is_the_ui_policy_without_the_video_origin()
    {
        var ui = Parse(DevServerPolicy("ui/angular.json"));
        var authUi = Parse(DevServerPolicy("auth-ui/angular.json"));

        authUi.Keys.Order().ShouldBe(ui.Keys.Order());
        foreach (var (directive, sources) in ui)
            authUi[directive].ShouldBe(sources.Where(s => s != DevJitsiOrigin).ToArray(), $"{directive} farklı");
    }

    [Theory]
    [InlineData("ui/angular.json")]
    [InlineData("auth-ui/angular.json")]
    public void Policy_locks_down_scripts_objects_and_base(string angularJsonPath)
    {
        var policy = Parse(DevServerPolicy(angularJsonPath));

        policy["script-src"].ShouldNotContain("'unsafe-inline'");
        policy["script-src"].ShouldNotContain("'unsafe-eval'");
        policy["object-src"].ShouldBe(["'none'"]);
        policy["base-uri"].ShouldBe(["'self'"]);
        policy["default-src"].ShouldBe(["'self'"]);
    }

    [Theory]
    [InlineData("ui/src/index.html")]
    [InlineData("auth-ui/src/index.html")]
    public void Index_html_has_no_inline_script(string indexPath)
    {
        // script-src hash/nonce taşımıyor: satır içi betik (ve on* olay özniteliği) CSP'ye takılır → ayrı dosya kullanın.
        var html = RepoFile(indexPath);
        Regex.IsMatch(html, @"<script(?![^>]*\bsrc=)(?![^>]*type=""application/(ld\+)?json"")[^>]*>").ShouldBeFalse("satır içi <script>");
        Regex.IsMatch(html, @"\son[a-z]+\s*=", RegexOptions.IgnoreCase).ShouldBeFalse("satır içi olay özniteliği");
    }

    [Theory]
    [InlineData("ui/angular.json")]
    [InlineData("auth-ui/angular.json")]
    public void Production_build_does_not_inline_critical_css(string angularJsonPath)
    {
        // inlineCritical, stil <link>'lerine onload="this.media='all'" ekler — satır içi olay işleyicisi CSP'yi ihlal eder.
        using var doc = JsonDocument.Parse(RepoFile(angularJsonPath));
        var project = doc.RootElement.GetProperty("projects").EnumerateObject().First().Value;
        var styles = project.GetProperty("architect").GetProperty("build").GetProperty("configurations")
            .GetProperty("production").GetProperty("optimization").GetProperty("styles");
        styles.GetProperty("inlineCritical").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void Nginx_sends_enforced_anti_framing_headers_in_every_location()
    {
        var template = NginxTemplate();
        var locations = LocationCount();
        locations.ShouldBe(3);
        Regex.Matches(template, @"add_header Content-Security-Policy ""frame-ancestors 'self'"" always;").Count.ShouldBe(locations);
        Regex.Matches(template, @"add_header X-Frame-Options ""SAMEORIGIN"" always;").Count.ShouldBe(locations);
        Regex.Matches(template, @"add_header X-Content-Type-Options ""nosniff"" always;").Count.ShouldBe(locations);
    }

    [Theory]
    [InlineData("ui/angular.json")]
    [InlineData("auth-ui/angular.json")]
    public void Dev_server_sends_enforced_anti_framing_headers(string angularJsonPath)
    {
        using var doc = JsonDocument.Parse(RepoFile(angularJsonPath));
        var headers = doc.RootElement.GetProperty("projects").EnumerateObject().First().Value
            .GetProperty("architect").GetProperty("serve").GetProperty("options").GetProperty("headers");
        headers.GetProperty("Content-Security-Policy").GetString().ShouldBe("frame-ancestors 'self'");
        headers.GetProperty("X-Frame-Options").GetString().ShouldBe("SAMEORIGIN");
    }

    [Fact]
    public void Nginx_template_only_substitutes_the_jitsi_origin_and_is_validated()
    {
        // nginx imajı yalnız TANIMLI ortam değişkenlerini değiştirir; başka bir ${...} ya ham kalır ya da bir nginx değişkenini yutar.
        Regex.Matches(NginxTemplate(), @"\$\{([A-Z0-9_]+)\}").Select(m => m.Groups[1].Value).Distinct()
            .ShouldBe(["CSP_JITSI_ORIGIN"]);

        var dockerfile = RepoFile("deploy/dockerfiles/angular-spa.Dockerfile");
        dockerfile.ShouldContain("COPY deploy/nginx/angular-spa.conf.template /etc/nginx/templates/default.conf.template");
        dockerfile.ShouldContain("/docker-entrypoint.d/15-validate-csp-env.sh");
        RepoFile("deploy/nginx/15-validate-csp-env.sh").ShouldContain("^https://[A-Za-z0-9.-]+(:[0-9]{1,5})?$");
    }
}
