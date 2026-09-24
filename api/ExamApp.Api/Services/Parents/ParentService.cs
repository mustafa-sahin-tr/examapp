using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services.Parents;

/// <summary>
/// issue #277 (madde 3). Parent satırı + rol event'i tek SaveChanges'te (atomik; EF kendi transaction'ını execution strategy
/// altında açar, değişiklikleri yalnızca başarıda kabul eder → retry güvenli). Sıra ParentController'da: Keycloak
/// SetRoleAsync → bu metot. SetRole başarısızsa hiçbir şey yazılmaz; bu yazım başarısızsa rol atanmış ama satır/event yok —
/// kullanıcının tekrar denemesi ikisini de yazar (Teacher/Student akışlarıyla aynı, bkz. UserRoleChangeRecorder).
/// </summary>
public sealed class ParentService : IParentService
{
    private readonly AppDbContext _context;

    public ParentService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> RegisterAsync(int userId, UserRoleChangeRequest roleChange, CancellationToken ct = default)
    {
        var parent = await _context.Parents.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (parent == null)
        {
            parent = new Parent { UserId = userId };
            _context.Parents.Add(parent);
        }

        if (UserRoleChangeOutbox.IsChange(roleChange, UserRole.Parent))
            _context.OutboxMessages.Add(UserRoleChangeOutbox.Create(roleChange, UserRole.Parent, Guid.NewGuid(), DateTime.UtcNow));

        if (_context.ChangeTracker.HasChanges())
            await _context.SaveChangesAsync(ct);

        return parent.Id;
    }

    public Task<bool> HasParentRecordAsync(int userId, CancellationToken ct = default)
        => _context.Parents.AsNoTracking().AnyAsync(p => p.UserId == userId, ct);
}
