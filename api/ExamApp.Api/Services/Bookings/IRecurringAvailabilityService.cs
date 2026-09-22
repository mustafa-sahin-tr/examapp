using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Bookings;

namespace ExamApp.Api.Services.Bookings;

/// <summary>
/// Tekrarlayan haftalık müsaitlik kuralları (issue #178). Kural oluşturulunca somut
/// <c>TeacherAvailabilitySlot</c> satırları 90 günlük ufka kadar üretilir; pencere
/// <see cref="TopUpAsync"/> ile (öğretmen kendi slotlarını listelerken) ileri kaydırılır.
/// Tüm metotlar çağıranın <c>userId</c>'sini alır ve sahiplik kontrolünü kendi içinde yapar.
/// </summary>
public interface IRecurringAvailabilityService
{
    /// <summary>
    /// Kural oluşturur ve EffectiveFrom'dan ufka kadar somut slotları üretir. Mevcut bir slotla
    /// çakışan haftalar atlanır ve <c>SkippedDates</c>'te listelenir. Onaysız öğretmen Forbidden alır.
    /// </summary>
    Task<RecurringAvailabilityRuleResultDto> CreateRuleAsync(
        int teacherUserId, CreateRecurringAvailabilityRuleDto dto, CancellationToken ct = default);

    /// <summary>Öğretmenin aktif kuralları (gün, başlangıç saati sırasıyla).</summary>
    Task<RecurringAvailabilityRuleListResultDto> GetMyRulesAsync(
        int teacherUserId, int skip, int take, CancellationToken ct = default);

    /// <summary>
    /// "Tüm seri": kuralı pasifleştirip soft-delete eder; bugünden itibaren kuraldan üretilmiş ve
    /// aktif randevusu olmayan slotları siler. Randevulu slotlar korunur ve yanıtta listelenir.
    /// Başkasının kuralı Forbidden döner.
    /// </summary>
    Task<RecurringAvailabilityRuleDeleteResultDto> DeleteRuleAsync(
        int teacherUserId, int ruleId, CancellationToken ct = default);

    /// <summary>
    /// Öğretmenin aktif kuralları için ufuk içinde eksik kalan occurrence'ları tamamlar. İdempotent:
    /// aynı kural+tarih için (soft-delete edilmiş satır dahil) ikinci kez üretmez. Üretilen slot sayısını döner.
    /// </summary>
    Task<int> TopUpAsync(int teacherId, int teacherUserId, CancellationToken ct = default);
}
