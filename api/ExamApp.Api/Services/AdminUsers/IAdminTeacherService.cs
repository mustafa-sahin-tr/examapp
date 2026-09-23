using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>Admin öğretmen listesi (issue #152).</summary>
public interface IAdminTeacherService
{
    /// <summary>
    /// Sayfalı öğretmen listesi, Id'ye göre artan. <paramref name="schoolId"/> verilirse o okulun öğretmenleri;
    /// <paramref name="unassigned"/> true ise okul bağlantısı olmayanlar (SchoolId null). İkisi birlikte verilmez
    /// (controller 400 döner). <paramref name="page"/> &lt; 1 → 1; <paramref name="pageSize"/> [1, <see cref="AdminTeacherService.MaxPageSize"/>] aralığına kırpılır.
    /// </summary>
    Task<Paged<AdminTeacherListItemDto>> ListAsync(int page, int pageSize, int? schoolId, bool unassigned, CancellationToken ct = default);
}
