namespace ExamApp.Api.Models.Dtos.Admin;

public class SchoolDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
}

public class UpsertSchoolDto
{
    public string Name { get; set; } = string.Empty;
    public string? City { get; set; }
}
