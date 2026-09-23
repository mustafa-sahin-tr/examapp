using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>Admin öğrenci listesi (issue #153).</summary>
public interface IAdminStudentService
{
    /// <summary>
    /// Sayfalı öğrenci listesi, Id'ye göre artan. <paramref name="schoolId"/> verilirse o okulun öğrencileri;
    /// <paramref name="unassigned"/> true ise okul bağlantısı olmayanlar (SchoolId null). İkisi birlikte verilmez
    /// (controller 400 döner). <paramref name="page"/> &lt; 1 → 1; <paramref name="pageSize"/> [1, <see cref="AdminListPaging.MaxPageSize"/>] aralığına kırpılır.
    /// </summary>
    Task<Paged<AdminStudentListItemDto>> ListAsync(int page, int pageSize, int? schoolId, bool unassigned, CancellationToken ct = default);
}
