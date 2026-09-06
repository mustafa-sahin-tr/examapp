using System;

namespace ExamApp.Api.Models.Dtos;

public class ResponseBaseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ObjectId { get; set; } = 0;

    /// <summary>
    /// True when the operation failed because the target resource does not exist
    /// or is not owned by the caller. Lets controllers map to HTTP 404 without
    /// leaking resource existence.
    /// </summary>
    public bool NotFound { get; set; } = false;

    /// <summary>
    /// True when the resource exists but the caller is not the owner/admin.
    /// Used where the endpoint explicitly requires 403 instead of the usual
    /// "don't leak existence" NotFound convention (e.g. issue #10).
    /// </summary>
    public bool Forbidden { get; set; } = false;

    /// <summary>
    /// True when the operation failed because it conflicts with existing state
    /// (e.g. a pending request already exists). Lets controllers map to HTTP 409.
    /// </summary>
    public bool Conflict { get; set; } = false;
}
