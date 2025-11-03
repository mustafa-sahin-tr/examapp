using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IStudentService
{
    Task<List<Grade>> GetGradesAsync();

    Task<StudentProfileDto> GetStudentProfile(int userId);

    Task<ResponseBaseDto> Save(int userId, RegisterStudentDto dto);

    Task<ResponseBaseDto> UpdateStudentGrade(int studentId, int gradeId);

    Task<List<StudentLookupDto>> GetStudentLookupsAsync();

    Task<UpdateThemeDto> UpdateStudentTheme(int userId, string themePreset, string? themeCustomConfig);
}
