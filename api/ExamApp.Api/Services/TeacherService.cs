using System;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

public class TeacherService : ITeacherService
{
    private readonly AppDbContext _context;

    public TeacherService(AppDbContext context) {
        _context = context;
    }

    public async Task<Teacher?> GetTeacher(int userId)
    {
        return await _context.Teachers            
            .Where(t => t.UserId == userId)
            .FirstOrDefaultAsync();
    }

    public async Task<ResponseBaseDto> Save(int userId, RegisterTeacherDto dto)
    {
        var existingTeacher = await _context.Teachers.FirstOrDefaultAsync(s => s.UserId == userId);
            if (existingTeacher != null)
            {
                return new ResponseBaseDto
                {
                    Success = false,
                    Message = "Öğretmen zaten kayıtlı."
                };
            }

            // 🔹 Yeni öğrenci kaydını ekle
            var teacher = new Teacher
            {
                UserId = userId,
                SchoolName = dto.SchoolName         
            };

            _context.Teachers.Add(teacher);
            await _context.SaveChangesAsync();
            return new ResponseBaseDto
            {
                Success = true,
                Message = "Öğretmen başarıyla kaydedildi.",
                ObjectId = teacher.Id            
            };
    }
    
}
