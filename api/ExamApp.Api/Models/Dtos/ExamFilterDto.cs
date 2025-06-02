using System;

namespace ExamApp.Api.Models.Dtos;

public class ExamFilterDto : FilterBaseDto
{
    public int? id = 0;
    public string? search = null;
    public List<int>? subjectIds = null;
    public List<int>? gradeIds = null;
    public int bookTestId = 0;
}
