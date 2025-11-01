using System.Collections.Generic;

namespace ExamApp.Api.Models.Requests;

public class BulkUserLookupRequest
{
    public List<int> UserIds { get; set; } = new();
}
