using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.StudyLinks;

namespace ExamApp.Api.Services.StudyLinks;

/// <summary>
/// Konu / alt konu harici çalışma linkleri (issue #61). Yönetim metotları Admin/Teacher içindir
/// (rol kontrolü controller'da; tüm konu/alt konular üzerinde yetkilidirler — sahiplik yok).
/// Öğrenci metodu sınav sahipliğini kendi içinde doğrular.
/// </summary>
public interface ITopicStudyLinkService
{
    /// <summary>Konu (yalnızca konu seviyesi) veya alt konu linkleri; SortOrder, Id sıralı.</summary>
    Task<TopicStudyLinkListResultDto> ListAsync(TopicStudyLinkQueryDto query, CancellationToken ct = default);

    Task<TopicStudyLinkResultDto> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Yeni link. Aktif eklemede kapsam başına en fazla 7 aktif link (Conflict + ActiveLimitReached).</summary>
    Task<TopicStudyLinkResultDto> CreateAsync(CreateTopicStudyLinkDto dto, UserProfileDto user, CancellationToken ct = default);

    /// <summary>Linki günceller. Pasif → aktif geçişinde 7-aktif-link limiti uygulanır.</summary>
    Task<TopicStudyLinkResultDto> UpdateAsync(int id, UpdateTopicStudyLinkDto dto, int userId, CancellationToken ct = default);

    /// <summary>Soft delete.</summary>
    Task<ResponseBaseDto> DeleteAsync(int id, int userId, CancellationToken ct = default);

    /// <summary>Kapsamdaki linklerin SortOrder'ını toplu günceller; güncel listeyi döner.</summary>
    Task<TopicStudyLinkListResultDto> ReorderAsync(ReorderTopicStudyLinksDto dto, int userId, CancellationToken ct = default);

    /// <summary>
    /// Öğrencinin tamamlanmış sınavında YANLIŞ cevapladığı sorular için, sorunun alt konularına göre gruplanmış
    /// aktif linkler. Sınav çağırana ait değilse NotFound (varlık sızdırılmaz). Sınav tamamlanmadıysa boş liste.
    /// Hiç linki olmayan grup/soru listeye girmez.
    /// </summary>
    Task<StudyLinkSuggestionsResultDto> GetSuggestionsForResultAsync(int testInstanceId, int studentUserId, CancellationToken ct = default);
}
