using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Hubs;

/// <summary>
/// Bağlantı kurmak için geçerli bir Keycloak JWT şart (bkz. Program.cs — OnMessageReceived
/// query string'den access_token'ı alır). [Authorize] olmadan anonim istemciler bağlanabiliyordu;
/// Admin grubu üyeliği bugüne kadar sadece IsInRole("Admin") == false olduğu için "kazara" korunuyordu
/// — gerçek bir auth kapısı değildi (issue #94 security review).
/// </summary>
[Authorize]
public class BadgeNotificationHub : Hub
{
    /// <summary>
    /// Rol bazlı bildirim grubu (issue #94 — bağımsız öğretmen başvurusu Admin'lere push edilir).
    /// <see cref="TeacherApplicationSubmittedConsumer"/> bu gruba <c>Clients.Group(AdminGroup)</c> ile yayın yapar.
    /// </summary>
    public const string AdminGroup = "role:Admin";

    private readonly ILogger<BadgeNotificationHub> _logger;

    public BadgeNotificationHub(ILogger<BadgeNotificationHub> logger)
    {
        _logger = logger;

    }

    public override async Task OnConnectedAsync()
    {
        var isAdmin = Context.User?.IsInRole("Admin") == true;

        // PII (claim değerleri: mail, kullanıcı adı vb.) log'a yazılmaz — sadece bağlantı id'si ve
        // Admin rolü boolean'ı (issue #94 security review).
        _logger.LogDebug("SignalR bağlantısı kuruldu. ConnectionId: {ConnectionId}, IsAdmin: {IsAdmin}",
            Context.ConnectionId,
            isAdmin);

        // Rol bazlı hedefleme (issue #94): KeycloakRoleTransformer (BadgeService.Security,
        // Program.cs'te AddScoped<IClaimsTransformation, ...> ile kayıtlı) realm_access.roles'ü
        // ClaimTypes.Role'e projekte eder; burada IsInRole("Admin") o sayede çalışır.
        if (isAdmin)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroup);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("SignalR bağlantısı kapandı. ConnectionId: {ConnectionId}, Reason: {Reason}",
            Context.ConnectionId,
            exception?.Message ?? "normal");

        await base.OnDisconnectedAsync(exception);
    }
}
