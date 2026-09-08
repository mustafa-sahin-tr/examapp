using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Practice;

/// <summary>
/// "Soru Çöz" pratik oturumu (issue #62): worksheet'ten bağımsız rastgele soru getirme ve cevap loglama.
/// Outbox event üretmez, puan/rozet etkilemez (epic #60 MVP kapsamı).
/// </summary>
public interface IPracticeSessionService
{
    /// <exception cref="InvalidOperationException">Öğrencinin sınıfı yoksa.</exception>
    Task<PracticeSessionDto> StartAsync(StudentProfileDto student, PracticeSessionStartDto request, CancellationToken ct = default);

    /// <returns>null: oturum yok ya da bu öğrenciye ait değil.</returns>
    Task<PracticeSessionDto?> GetAsync(int sessionId, int studentId, CancellationToken ct = default);

    /// <summary>
    /// Bu oturumda henüz gösterilmemiş rastgele bir soru döner ve onu "gösterildi" olarak kaydeder.
    /// Cevaplanmamış bekleyen bir soru varsa yenisini seçmek yerine onu tekrar döner.
    /// Havuz bittiğinde <see cref="PracticeNextQuestionDto.PoolExhausted"/> = true.
    /// </summary>
    /// <returns>null: oturum yok ya da bu öğrenciye ait değil.</returns>
    /// <exception cref="InvalidOperationException">Oturum sonlandırılmışsa.</exception>
    Task<PracticeNextQuestionDto?> NextQuestionAsync(int sessionId, int studentId, CancellationToken ct = default);

    /// <returns>null: oturum yok ya da bu öğrenciye ait değil.</returns>
    /// <exception cref="InvalidOperationException">Soru bu oturumda gösterilmemiş/zaten cevaplanmış, oturum bitmiş ya da şık soruya ait değilse.</exception>
    Task<PracticeAnswerResultDto?> SubmitAnswerAsync(int sessionId, int studentId, PracticeAnswerSubmitDto dto, CancellationToken ct = default);

    /// <returns>null: oturum yok ya da bu öğrenciye ait değil.</returns>
    Task<PracticeSessionDto?> EndAsync(int sessionId, int studentId, CancellationToken ct = default);
}
