using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;

public class AppDbContext : DbContext
{
    private int? _currentUserId = 0;  // Varsayılan olarak 0
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) 
    {
    }

    // BaseController'dan çağrılacak metod
    public void SetCurrentUser(int userId)
    {
        _currentUserId = userId;
    }

    public override int SaveChanges()
    {
        ApplyAuditInfo();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditInfo();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditInfo()
    {
        var entries = ChangeTracker.Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreateTime = DateTime.UtcNow;
                entry.Entity.CreateUserId = _currentUserId;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdateTime = DateTime.UtcNow;
                entry.Entity.UpdateUserId = _currentUserId;
            }
            else if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified; // Soft delete yap
                entry.Entity.IsDeleted = true;
                entry.Entity.DeleteTime = DateTime.UtcNow;
                entry.Entity.DeleteUserId = _currentUserId;
            }
        }
    }
    public DbSet<User> Users { get; set; } // Kullanıcı tablosu
    public DbSet<Student> Students { get; set; } // Öğrenci tablosu
    public DbSet<Teacher> Teachers { get; set; } // Öğretmen tablosu
    public DbSet<Parent> Parents { get; set; } // Veli tablosu
    public DbSet<Worksheet> Worksheets { get; set; }
    public DbSet<Question> Questions { get; set; }
    public DbSet<Answer> Answers { get; set; }
    public DbSet<WorksheetQuestion> TestQuestions { get; set; }
    public DbSet<Grade> Grades { get; set; }
    public DbSet<Subject> Subjects { get; set; }
    public DbSet<GradeSubject> GradeSubjects { get; set; }
    public DbSet<Topic> Topics { get; set; }
    public DbSet<SubTopic> SubTopics { get; set; }
    public DbSet<WorksheetInstance> TestInstances { get; set; }
    public DbSet<WorksheetInstanceQuestion> TestInstanceQuestions { get; set; }
    public DbSet<WorksheetPrototype> TestPrototypes { get; set; }
    public DbSet<WorksheetPrototypeDetail> TestPrototypeDetail { get; set; }
    public DbSet<StudentPoint> StudentPoints { get; set; }
    public DbSet<StudentPointHistory> StudentPointHistories { get; set; }
    public DbSet<Reward> Rewards { get; set; }
    public DbSet<StudentReward> StudentRewards { get; set; }
    public DbSet<Leaderboard> Leaderboards { get; set; }
    public DbSet<SpecialEvent> SpecialEvents { get; set; }
    public DbSet<StudentSpecialEvent> StudentSpecialEvents { get; set; }
    public DbSet<StudentBadge> StudentBadges { get; set; }
    public DbSet<Book> Books { get; set; }
    public DbSet<BookTest> BookTests { get; set; }
    public DbSet<Passage> Passage { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Grade>().HasData(
            new Grade { Id = 1, Name = "1. Sınıf" },
            new Grade { Id = 2, Name = "2. Sınıf" },
            new Grade { Id = 3, Name = "3. Sınıf" },
            new Grade { Id = 4, Name = "4. Sınıf" },
            new Grade { Id = 5, Name = "5. Sınıf" },
            new Grade { Id = 6, Name = "6. Sınıf" },
            new Grade { Id = 7, Name = "7. Sınıf" },
            new Grade { Id = 8, Name = "8. Sınıf" },
            new Grade { Id = 9, Name = "9. Sınıf" },
            new Grade { Id = 10, Name = "10. Sınıf" },
            new Grade { Id = 11, Name = "11. Sınıf" },
            new Grade { Id = 12, Name = "12. Sınıf" }
        );

        modelBuilder.Entity<Subject>().HasData(
            new Subject { Id = 1, Name = "Türkçe" },
            new Subject { Id = 2, Name = "Matematik" },
            new Subject { Id = 3, Name = "Hayat Bilgisi" },
            new Subject { Id = 4, Name = "Fen Bilimleri" },
            new Subject { Id = 5, Name = "Sosyal Bilgiler" },
            new Subject { Id = 6, Name = "T.C. İnkılâp Tarihi ve Atatürkçülük" },
            new Subject { Id = 7, Name = "Yabancı Dil" },
            new Subject { Id = 8, Name = "Din Kültürü ve Ahlak Bilgisi" },
            new Subject { Id = 9, Name = "Türk Dili ve Edebiyatı" },
            new Subject { Id = 10, Name = "Tarih" },
            new Subject { Id = 11, Name = "Coğrafya" },
            new Subject { Id = 12, Name = "Fizik" },
            new Subject { Id = 13, Name = "Kimya" },
            new Subject { Id = 14, Name = "Biyoloji" },
            new Subject { Id = 15, Name = "Felsefe" }
        );

        modelBuilder.Entity<GradeSubject>().HasData(            
            new GradeSubject { Id = 1, GradeId = 1, SubjectId = 1 }, // 1. Sınıf - Türkçe
            new GradeSubject { Id = 2, GradeId = 1, SubjectId = 2 }, // 1. Sınıf - Matematik
            new GradeSubject { Id = 3, GradeId = 1, SubjectId = 3 }, // 1. Sınıf - Hayat Bilgisi
            new GradeSubject { Id = 4, GradeId = 2, SubjectId = 1 },
            new GradeSubject { Id = 5, GradeId = 2, SubjectId = 2 },
            new GradeSubject { Id = 6, GradeId = 2, SubjectId = 3 },
            new GradeSubject { Id = 7, GradeId = 3, SubjectId = 1 },
            new GradeSubject { Id = 8, GradeId = 3, SubjectId = 2 },
            new GradeSubject { Id = 9, GradeId = 3, SubjectId = 3 },
            new GradeSubject { Id = 10, GradeId = 3, SubjectId = 4 },
            new GradeSubject { Id = 11, GradeId = 4, SubjectId = 1 },
            new GradeSubject { Id = 12, GradeId = 4, SubjectId = 2 },
            new GradeSubject { Id = 13, GradeId = 4, SubjectId = 4 },
            new GradeSubject { Id = 14, GradeId = 4, SubjectId = 5 },

            // 🟢 Ortaokul Dersleri (5-8. Sınıf)
            new GradeSubject { Id = 15, GradeId = 5, SubjectId = 1 },
            new GradeSubject { Id = 16, GradeId = 5, SubjectId = 2 },
            new GradeSubject { Id = 17, GradeId = 5, SubjectId = 4 },
            new GradeSubject { Id = 18, GradeId = 5, SubjectId = 5 },
            new GradeSubject { Id = 19, GradeId = 5, SubjectId = 7 },
            new GradeSubject { Id = 20, GradeId = 6, SubjectId = 1 },
            new GradeSubject { Id = 21, GradeId = 6, SubjectId = 2 },
            new GradeSubject { Id = 22, GradeId = 6, SubjectId = 4 },
            new GradeSubject { Id = 23, GradeId = 6, SubjectId = 5 },

            // 🟢 Lise Dersleri (9-12. Sınıf)
            new GradeSubject { Id = 24, GradeId = 9, SubjectId = 9 },
            new GradeSubject { Id = 25, GradeId = 9, SubjectId = 10 },
            new GradeSubject { Id = 26, GradeId = 9, SubjectId = 11 },
            new GradeSubject { Id = 27, GradeId = 9, SubjectId = 2 },
            new GradeSubject { Id = 28, GradeId = 9, SubjectId = 12 },
            new GradeSubject { Id = 29, GradeId = 9, SubjectId = 13 },
            new GradeSubject { Id = 30, GradeId = 9, SubjectId = 14 },
            new GradeSubject { Id = 31, GradeId = 9, SubjectId = 7 },
            new GradeSubject { Id = 32, GradeId = 10, SubjectId = 9 },
            new GradeSubject { Id = 33, GradeId = 10, SubjectId = 10 },
            new GradeSubject { Id = 34, GradeId = 10, SubjectId = 11 },
            new GradeSubject { Id = 35, GradeId = 10, SubjectId = 2 },
            new GradeSubject { Id = 36, GradeId = 10, SubjectId = 12 },
            new GradeSubject { Id = 37, GradeId = 10, SubjectId = 13 },
            new GradeSubject { Id = 38, GradeId = 10, SubjectId = 14 },
            new GradeSubject { Id = 39, GradeId = 10, SubjectId = 7 },
            new GradeSubject { Id = 40, GradeId = 11, SubjectId = 9 },
            new GradeSubject { Id = 41, GradeId = 11, SubjectId = 10 },
            new GradeSubject { Id = 42, GradeId = 11, SubjectId = 12 },
            new GradeSubject { Id = 43, GradeId = 11, SubjectId = 13 },
            new GradeSubject { Id = 44, GradeId = 11, SubjectId = 14 },
            new GradeSubject { Id = 45, GradeId = 11, SubjectId = 15 }
        );

        TopicSeed.SeedData(modelBuilder);

        modelBuilder.Entity<Student>()
            .HasMany(s => s.StudentPoints)  // 🟢 Bir Student'in birden fazla StudentPoints kaydı vardır.
            .WithOne(sp => sp.Student)  // 🟢 Bir StudentPoints yalnızca bir Student'e bağlıdır.
            .HasForeignKey(s => s.StudentId);  // 🟢 Foreign Key tanımlaması

        modelBuilder.Entity<Question>()
            .HasOne(q => q.Subject)
            .WithMany(c => c.Questions)
            .HasForeignKey(q => q.SubjectId);

        modelBuilder.Entity<Answer>()
            .HasOne(a => a.Question)
            .WithMany(q => q.Answers)
            .HasForeignKey(a => a.QuestionId);

        modelBuilder.Entity<WorksheetQuestion>()
            .HasOne(tq => tq.Worksheet)
            .WithMany(t => t.WorksheetQuestions)
            .HasForeignKey(tq => tq.TestId);

        modelBuilder.Entity<WorksheetQuestion>()
            .HasOne(tq => tq.Question)
            .WithMany(q => q.WorksheetQuestions)
            .HasForeignKey(tq => tq.QuestionId);


            // 📌 Grade - Subject İlişkisi
        modelBuilder.Entity<GradeSubject>()
            .HasOne(gs => gs.Grade)
            .WithMany(g => g.GradeSubjects)
            .HasForeignKey(gs => gs.GradeId);

        modelBuilder.Entity<GradeSubject>()
            .HasOne(gs => gs.Subject)
            .WithMany(s => s.GradeSubjects)
            .HasForeignKey(gs => gs.SubjectId);


        modelBuilder.Entity<StudentPoint>()
            .HasIndex(sp => sp.StudentId)
            .IsUnique();

        modelBuilder.Entity<StudentPointHistory>()
            .HasOne(sph => sph.Student)
            .WithMany()
            .HasForeignKey(sph => sph.StudentId);

        modelBuilder.Entity<StudentReward>()
            .HasOne(sr => sr.Student)
            .WithMany()
            .HasForeignKey(sr => sr.StudentId);

        modelBuilder.Entity<StudentReward>()
            .HasOne(sr => sr.Reward)
            .WithMany(r => r.StudentRewards)
            .HasForeignKey(sr => sr.RewardId);

        modelBuilder.Entity<Leaderboard>()
            .HasOne(lb => lb.Student)
            .WithMany()
            .HasForeignKey(lb => lb.StudentId);

        modelBuilder.Entity<StudentSpecialEvent>()
            .HasOne(sse => sse.Student)
            .WithMany()
            .HasForeignKey(sse => sse.StudentId);

        modelBuilder.Entity<StudentSpecialEvent>()
            .HasOne(sse => sse.SpecialEvent)
            .WithMany()
            .HasForeignKey(sse => sse.SpecialEventId);

        modelBuilder.Entity<Student>()
            .HasMany(s => s.StudentBadges)  
            .WithOne(sb => sb.Student)  
            .HasForeignKey(sb => sb.StudentId);

        modelBuilder.Entity<Badge>()
            .HasMany(b => b.StudentBadges)
            .WithOne(sb => sb.Badge)
            .HasForeignKey(sb => sb.BadgeId);

        modelBuilder.Entity<Passage>()
            .HasMany(p => p.Questions)
            .WithOne(q => q.Passage)
            .HasForeignKey(q => q.PassageId);

        modelBuilder.Entity<Question>()
            .HasMany(q => q.Answers)
            .WithOne(a => a.Question)
            .HasForeignKey(a => a.QuestionId)
            .OnDelete(DeleteBehavior.Cascade); // Eğer soru silinirse cevaplar da silinsin

        modelBuilder.Entity<Question>()
            .HasOne(q => q.CorrectAnswer)  // Doğru cevap için ayrı ilişki
            .WithMany() // Burada WithMany() ile ilişkiyi tek yönlü yapıyoruz!
            .HasForeignKey(q => q.CorrectAnswerId)
            .OnDelete(DeleteBehavior.Restrict); // Döngüsel bağımlılığı önlemek için

        modelBuilder.Entity<Worksheet>()
            .HasOne(q => q.BookTest)
            .WithMany()
            .HasForeignKey(q => q.BookTestId)
            .OnDelete(DeleteBehavior.Restrict);  // Silme işlemi sırasında bağımsız kalmasını sağlıyoruz

        modelBuilder.Entity<BookTest>()
            .HasOne(bt => bt.Book)
            .WithMany(b => b.BookTests)
            .HasForeignKey(bt => bt.BookId)
            .OnDelete(DeleteBehavior.Cascade); // Eğer bir kitap silinirse, testleri de silinsin.

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Eğer entity BaseEntity sınıfından türemişse
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                // Doğru tür dönüşümüyle HasQueryFilter ekle
                var method = typeof(AppDbContext).GetMethod(nameof(SetGlobalQueryFilter),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    ?.MakeGenericMethod(entityType.ClrType);

                method?.Invoke(null, new object[] { modelBuilder });
            }
        }
            
    }

    private static LambdaExpression ConvertFilter<T>(Expression<Func<T, bool>> filter)
    {
        return filter;
    }

    private static void SetGlobalQueryFilter<T>(ModelBuilder modelBuilder) where T : BaseEntity
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => !e.IsDeleted);
    }
}

public class Exam
{
    public int Id { get; set; }
    public string Name { get; set; }
}
