using System;

namespace ExamApp.Api.Models.Dtos;

public class TestStartResultDto : ResponseBaseDto
{
    public int InstanceId { get; set; }
    public DateTime StartTime { get; set; }
}

