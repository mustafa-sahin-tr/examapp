using ExamApp.Api.Data;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.UserRoles;

/// <summary>
/// issue #277 (madde 4) — sıra kararı: Teacher/Student register akışlarında Keycloak rolü, kayıt doğrulamaları geçip kayıt
/// satırı commit edildikten SONRA verilir (#234: reddedilen kayıt rol almamalı). Rol event'i bu yüzden kayıt
/// transaction'ına DEĞİL, <c>SetRoleAsync</c> başarılı olduktan sonra kendi (tek) SaveChanges'ine yazılır:
/// <list type="bullet">
/// <item>SetRoleAsync başarısız → event yok (auth-api'ye henüz atanmamış bir rol bildirilmez); istek 500, kullanıcı tekrar
/// denediğinde kayıt idempotent geçer ve rol + event yeniden denenir.</item>
/// <item>SetRoleAsync başarılı, bu yazım başarısız → istek 500; Keycloak rolü atanmış ama event yok. Kullanıcının register
/// tekrarı (profil rolü hâlâ eski → değişiklik) event'i yazar; aksi halde auth-api rolü login/token-exchange senkronunda alır.</item>
/// </list>
/// Tek SaveChanges kullanıcı transaction'ı açmaz: EF onu execution strategy (Aspire Npgsql retry) altında kendi
/// transaction'ında çalıştırır ve değişiklikleri yalnızca başarıda kabul eder → retry güvenli.
/// </summary>
public sealed class UserRoleChangeRecorder : IUserRoleChangeRecorder
{
    private readonly AppDbContext _context;

    public UserRoleChangeRecorder(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> RecordIfChangedAsync(UserRoleChangeRequest request, UserRole newRole, CancellationToken ct = default)
    {
        if (!UserRoleChangeOutbox.IsChange(request, newRole))
            return false;

        _context.OutboxMessages.Add(UserRoleChangeOutbox.Create(request, newRole, Guid.NewGuid(), DateTime.UtcNow));
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
