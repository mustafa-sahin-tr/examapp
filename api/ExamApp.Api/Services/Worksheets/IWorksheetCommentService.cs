using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.WorksheetComments;

namespace ExamApp.Api.Services.Worksheets;

/// <summary>Yorum uçlarını çağıranın türü — controller JWT'yle doğrulanmış etkin rolden (EffectiveRole) belirler.</summary>
public enum WorksheetCommentActorKind
{
    Student = 0,
    Teacher = 1,

    /// <summary>Öğretmen/öğrenci olmayan admin: tüm thread'leri okur, yazmaz (moderasyon kapsam dışı).</summary>
    AdminReader = 2
}

/// <param name="UserId">exam/auth user id (Student.UserId / Teacher.UserId).</param>
/// <param name="KeycloakId">Keycloak sub — yalnızca yazılan kayda (AuthorKeycloakId) gider, DTO'ya çıkmaz.</param>
/// <param name="FullName">Profildeki görünen ad (POST yanıtındaki authorDisplayName için).</param>
/// <param name="IsAdmin">Token'daki Admin rolü — öğretmen okuma muafiyeti (WorksheetAccess.CanView ile aynı). Yazma muafiyeti vermez.</param>
public sealed record WorksheetCommentActor(int UserId, string KeycloakId, string? FullName, WorksheetCommentActorKind Kind, bool IsAdmin);

/// <summary>
/// issue #105 (dilim 1): worksheet / soru yorum-soru thread'leri — okuma, yazma ve yetki. Bildirim (outbox) dilim 2.
/// <para>Okuma: öğrenci — mevcut test başlatma kuralı (<c>WorksheetAccess.CanStudentStartTest</c>: aktif atama VEYA grade uyumlu +
/// Normal görünürlük) VEYA worksheet'i daha önce çözmüş (instance'ı var). Öğretmen — <c>WorksheetAccess.CanView</c> (sahip/admin/
/// paylaşım, #11/#191) VEYA worksheet'e atama yapmış. Admin — hepsi. Retire (soft-delete) edilmiş worksheet'te öğrenci
/// okuma/yazması yalnızca instance'ı olana açık; öğretmen kuralları aynı.</para>
/// <para>Öğrenci yazma: etkin ayar açık (<c>ilgili aktif atama.CommentsEnabledOverride ?? Worksheet.CommentsEnabled</c>); soru
/// thread'inde worksheet başlatılmış VE soru cevaplanmış (SelectedAnswerId veya AnswerPayload dolu); worksheet seviyesinde şart yok.
/// Başka öğrencinin köküne reply serbest.</para>
/// <para>Öğretmen yazma (ayar kapalıyken de): kök yorum — worksheet sahibi veya worksheet'e aktif ataması olan öğretmen;
/// reply — kök yazarı öğrenciyse kök yazılırken sabitlenen ilgili öğretmen (<c>WorksheetComment.ResponsibleTeacherUserId</c>,
/// <see cref="IWorksheetResponsibleTeacherResolver"/> ile çözülür), kök yazarı öğretmense kökün yazarı veya kök açabilen öğretmen.
/// Admin'e yazma muafiyeti yok.</para>
/// </summary>
public interface IWorksheetCommentService
{
    Task<WorksheetCommentPageResultDto> GetThreadAsync(int worksheetId, WorksheetCommentQueryDto query, WorksheetCommentActor actor, CancellationToken ct = default);

    /// <summary>Bir kökün reply'ları, eskiden yeniye, cursor'lı. Okuma yetkisi thread ile aynı; rootId bu worksheet'in kökü olmalı.</summary>
    Task<WorksheetCommentRepliesResultDto> GetRepliesAsync(int worksheetId, int rootId, WorksheetCommentRepliesQueryDto query, WorksheetCommentActor actor, CancellationToken ct = default);

    Task<WorksheetCommentResultDto> CreateAsync(int worksheetId, CreateWorksheetCommentDto dto, WorksheetCommentActor actor, CancellationToken ct = default);
}
