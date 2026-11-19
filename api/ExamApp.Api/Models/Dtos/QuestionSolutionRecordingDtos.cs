using System;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #1226: çözüm kaydı (ses ayrı uçtan okunur).</summary>
public class QuestionSolutionRecordingDto
{
    public int QuestionId { get; set; }
    public int DurationMs { get; set; }
    public string AudioContentType { get; set; } = string.Empty;
    public long AudioSizeBytes { get; set; }
    public int SchemaRevision { get; set; }
    /// <summary>Çizgi belgesi: <c>{v,width,height,strokes:[{c,t,e?,p:[x,y,dt,...]}]}</c>.</summary>
    public JsonElement Strokes { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class QuestionSolutionRecordingFormDto
{
    public IFormFile? Audio { get; set; }
    /// <summary>Çizgi belgesi (JSON metni).</summary>
    public string? Strokes { get; set; }
    public int DurationMs { get; set; }
}
