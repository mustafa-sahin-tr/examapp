using System;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface ITeacherService
{
    Task<ResponseBaseDto> Save(int userId, RegisterTeacherDto dto);
}
