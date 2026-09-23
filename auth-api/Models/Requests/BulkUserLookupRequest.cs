using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Requests;

public class BulkUserLookupRequest
{
    // Issue #219: tek çağrıda sınırsız id ile tüm kullanıcı tablosunu çekmeyi engeller.
    [MaxLength(500)]
    public List<int> UserIds { get; set; } = new();

    /// <summary>
    /// Issue #152: true ise yanıttaki <c>Enabled</c> Keycloak admin API'den (kullanıcı başı GET, sınırlı paralel,
    /// süre bütçeli) doldurulur. Varsayılan false — mevcut çağıranlar Keycloak'a ek yük bindirmez, <c>Enabled</c> null kalır.
    /// </summary>
    public bool IncludeAccountStatus { get; set; }
}
