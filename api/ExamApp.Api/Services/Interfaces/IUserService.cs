using System;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IUserService
{
    Task<ResponseBaseDto> UpdateUserAvatar(int userId, string avatarUrl);
}
