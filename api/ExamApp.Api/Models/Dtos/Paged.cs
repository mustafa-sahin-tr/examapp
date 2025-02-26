using System;

namespace ExamApp.Api.Models.Dtos;

public class Paged<T>
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<T> Items { get; set; }
}
