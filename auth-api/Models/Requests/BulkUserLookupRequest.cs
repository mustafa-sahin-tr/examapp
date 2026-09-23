using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Models.Requests;

public class BulkUserLookupRequest
{
    // Issue #219: tek çağrıda sınırsız id ile tüm kullanıcı tablosunu çekmeyi engeller.
    [MaxLength(500)]
    public List<int> UserIds { get; set; } = new();
}
