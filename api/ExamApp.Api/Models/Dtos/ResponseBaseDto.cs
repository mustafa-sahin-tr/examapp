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
}
