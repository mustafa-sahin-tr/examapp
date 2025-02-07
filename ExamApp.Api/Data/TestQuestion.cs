using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public class TestQuestion
{
    [Key]
    public int Id { get; set; }
    public int TestId { get; set; }
    public Test Test { get; set; }

    public int QuestionId { get; set; }
    public Question Question { get; set; }
}

