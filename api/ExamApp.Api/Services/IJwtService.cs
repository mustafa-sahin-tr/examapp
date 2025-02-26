using System;
using ExamApp.Api.Data;

namespace ExamApp.Api.Services;

public interface IJwtService
{
    string GenerateToken(User user);
}
