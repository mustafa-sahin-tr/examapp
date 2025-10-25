using System;
using System.Linq.Expressions;
using ExamApp.Foundation.Persistence;
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
    public DbSet<QuestionSubTopic> QuestionSubTopics { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }
    public DbSet<ProgramStep> ProgramSteps { get; set; } // ProgramStep tablosu
    public DbSet<ProgramStepOption> ProgramStepOptions { get; set; } // ProgramStepOption tablosu
    public DbSet<ProgramStepAction> ProgramStepActions { get; set; } // ProgramStepAction tablosu
    public DbSet<UserProgram> UserPrograms { get; set; } // UserProgram tablosu
    public DbSet<UserProgramSchedule> UserProgramSchedules { get; set; } // UserProgramSchedule tablosu
    public DbSet<LearningOutcomeDetail> LearningOutcomeDetails { get; set; } // LearningOutcomeDetail tablosu
    public DbSet<LearningOutcome> LearningOutcomes { get; set; } // LearningOutcome tablosu



    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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

        modelBuilder.Entity<Worksheet>()
            .HasMany(w => w.WorksheetQuestions)
            .WithOne(wq => wq.Worksheet)
            .HasForeignKey(wq => wq.TestId);

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

        modelBuilder.Entity<QuestionSubTopic>()
            .HasOne(qst => qst.Question)
            .WithMany(q => q.QuestionSubTopics)
            .HasForeignKey(qst => qst.QuestionId);

        modelBuilder.Entity<QuestionSubTopic>()
            .HasOne(qst => qst.SubTopic)
            .WithMany(st => st.QuestionSubTopics)
            .HasForeignKey(qst => qst.SubTopicId);

        // ProgramStep, ProgramStepOption, and ProgramStepAction relationships
        modelBuilder.Entity<ProgramStep>()
            .HasMany(ps => ps.Options)
            .WithOne(pso => pso.ProgramStep)
            .HasForeignKey(pso => pso.ProgramStepId)
            .OnDelete(DeleteBehavior.Cascade); // If a ProgramStep is deleted, its options are also deleted

        modelBuilder.Entity<ProgramStep>()
            .HasMany(ps => ps.Actions)
            .WithOne(psa => psa.ProgramStep)
            .HasForeignKey(psa => psa.ProgramStepId)
            .OnDelete(DeleteBehavior.Cascade); // If a ProgramStep is deleted, its actions are also deleted

        // UserProgram and UserProgramSchedule relationships
        modelBuilder.Entity<UserProgram>()
            .HasMany(up => up.Schedules)
            .WithOne(ups => ups.UserProgram)
            .HasForeignKey(ups => ups.UserProgramId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LearningOutcome>()
            .HasOne(lo => lo.SubTopic)
            .WithMany() // SubTopic içinde LearningOutcomes kolleksiyonu yoksa WithMany() kullanabiliriz
            .HasForeignKey(lo => lo.SubTopicId);

        modelBuilder.Entity<LearningOutcomeDetail>()
            .HasOne(lod => lod.LearningOutcome)
            .WithMany(lo => lo.Details)
            .HasForeignKey(lod => lod.LearningOutcomeId);

        // Call Seeders
        // TopicSeed.SeedGradesSubjects(modelBuilder); // Assuming TopicSeed is already using HasData
        // ProgramStepSeed.SeedData(modelBuilder); // New seeder for ProgramSteps
        // CatalogSeed.Initialize(); // Assuming CatalogSeed is already using HasData


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
