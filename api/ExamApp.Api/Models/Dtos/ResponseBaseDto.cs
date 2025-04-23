using System;

namespace ExamApp.Api.Models.Dtos;

public class ResponseBaseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
