using System;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<Exam> Exams { get; set; }

    public DbSet<User> Users { get; set; } // Kullanıcı tablosu
    public DbSet<Student> Students { get; set; } // Öğrenci tablosu
    public DbSet<Teacher> Teachers { get; set; } // Öğretmen tablosu
    public DbSet<Parent> Parents { get; set; } // Veli tablosu

    public DbSet<Category> Categories { get; set; }
    public DbSet<ExamTest> ExamTests { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<Answer> Answers { get; set; }
    public DbSet<TestQuestion> TestQuestions { get; set; }
    public DbSet<StudentAnswer> StudentAnswers { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Question>()
            .HasOne(q => q.Category)
            .WithMany(c => c.Questions)
            .HasForeignKey(q => q.CategoryId);

        modelBuilder.Entity<Answer>()
            .HasOne(a => a.Question)
            .WithMany(q => q.Answers)
            .HasForeignKey(a => a.QuestionId);

        modelBuilder.Entity<StudentAnswer>()
            .HasOne(sa => sa.Student)
            .WithMany()
            .HasForeignKey(sa => sa.StudentId);

        modelBuilder.Entity<StudentAnswer>()
            .HasOne(sa => sa.Question)
            .WithMany()
            .HasForeignKey(sa => sa.QuestionId);

        modelBuilder.Entity<StudentAnswer>()
            .HasOne(sa => sa.SelectedAnswer)
            .WithMany()
            .HasForeignKey(sa => sa.SelectedAnswerId);

        // 🟢 Many-to-Many ilişki (Test - Question)
        modelBuilder.Entity<TestQuestion>()
            .HasKey(tq => new { tq.TestId, tq.QuestionId }); // Composite Primary Key

        modelBuilder.Entity<TestQuestion>()
            .HasOne(tq => tq.Test)
            .WithMany(t => t.TestQuestions)
            .HasForeignKey(tq => tq.TestId);

        modelBuilder.Entity<TestQuestion>()
            .HasOne(tq => tq.Question)
            .WithMany(q => q.TestQuestions)
            .HasForeignKey(tq => tq.QuestionId);
    }
}

public class Exam
{
    public int Id { get; set; }
    public string Name { get; set; }
}
