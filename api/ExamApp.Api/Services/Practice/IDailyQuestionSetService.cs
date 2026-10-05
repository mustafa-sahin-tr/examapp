using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// "Günün soruları" (issue #99): öğrenci + yerel gün başına sabit, tembel üretilen soru seti. Çözme mevcut pratik oturumu
/// akışıyla (<see cref="IPracticeSessionService"/>) yapılır.
/// </summary>
public interface IDailyQuestionSetService
{
    /// <summary>Bugünün seti ve ilerlemesi; set yoksa üretir. Havuz boşsa/sınıf yoksa <c>Status = Empty</c> (hata değil).</summary>
    Task<DailySetDto> GetTodayAsync(StudentProfileDto student, CancellationToken ct = default);

    /// <summary>
    /// Bugünün setinin pratik oturumunu açar ya da mevcut oturumu döner (idempotent; eşzamanlı çağrılarda tek oturum).
    /// Kullanıcı oturumu set bitmeden sonlandırdıysa oturum yeniden açılır. Tamamlanmış sette yeni oturum açılmaz.
    /// </summary>
    /// <returns>null: set boş (havuzda soru yok / sınıf yok) — başlatılacak bir şey yok.</returns>
    Task<DailyStartResultDto?> StartTodayAsync(StudentProfileDto student, CancellationToken ct = default);
}
