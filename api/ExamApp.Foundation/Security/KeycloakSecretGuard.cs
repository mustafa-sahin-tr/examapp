using System;
using System.Linq;

namespace ExamApp.Foundation.Security;

/// <summary>
/// Startup fail-fast for Keycloak client secrets (issues #238, #372). Outside Development an empty
/// secret or one carrying the dev-only <c>devOnly</c> prefix (the shared convention of .env.example /
/// AppHost parameters / dev-import realm) is rejected, so a misconfigured deployment fails at boot
/// instead of with an opaque invalid_client on the first token request.
/// </summary>
public static class KeycloakSecretGuard
{
    public static bool IsMissingOrDevOnly(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("devOnly", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> naming every offending key when not Development.
    /// </summary>
    public static void EnsureConfigured(bool isDevelopment, params (string Key, string? Value)[] secrets)
    {
        if (isDevelopment)
            return;

        var bad = secrets.Where(s => IsMissingOrDevOnly(s.Value)).Select(s => s.Key).ToArray();
        if (bad.Length == 0)
            return;

        var envNames = string.Join(", ", bad.Select(k => k.Replace(":", "__")));
        throw new InvalidOperationException(
            $"{string.Join(", ", bad)} must be set via environment variable ({envNames}); " +
            "empty or dev-only values are not allowed outside Development.");
    }
}
