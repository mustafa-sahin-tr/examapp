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
    public DbSet<TeacherSubject> TeacherSubjects { get; set; } // Bağımsız öğretmen ↔ ders (issue #95)
    public DbSet<Topic> Topics { get; set; }
    public DbSet<SubTopic> SubTopics { get; set; }
    public DbSet<ClassifierCacheConfig> ClassifierCacheConfigs { get; set; }
    public DbSet<WorksheetInstance> TestInstances { get; set; }
    public DbSet<WorksheetInstanceQuestion> TestInstanceQuestions { get; set; }
    public DbSet<WorksheetPrototype> TestPrototypes { get; set; }
    public DbSet<WorksheetPrototypeDetail> TestPrototypeDetail { get; set; }
    public DbSet<WorksheetAssignment> WorksheetAssignments { get; set; }
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
    public DbSet<UserProgramStudyPageSchedule> UserProgramStudyPageSchedules { get; set; } // UserProgramStudyPageSchedule tablosu
    public DbSet<StudyItem> StudyItems { get; set; } // StudyItem tablosu
    public DbSet<StudyItemImage> StudyItemImages { get; set; } // StudyItemImage tablosu
    public DbSet<LearningOutcomeDetail> LearningOutcomeDetails { get; set; } // LearningOutcomeDetail tablosu
    public DbSet<LearningOutcome> LearningOutcomes { get; set; } // LearningOutcome tablosu

    public DbSet<QuestionTransferJob> QuestionTransferJobs { get; set; }
    public DbSet<QuestionTransferImportMap> QuestionTransferImportMaps { get; set; }

    public DbSet<QuestionTransferExportBundle> QuestionTransferExportBundles { get; set; }
    public DbSet<QuestionTransferExportMap> QuestionTransferExportMaps { get; set; }

    public DbSet<WorksheetReminder> WorksheetReminders { get; set; }

    public DbSet<WorksheetAccessRequest> WorksheetAccessRequests { get; set; }
    public DbSet<WorksheetAccessGrant> WorksheetAccessGrants { get; set; }

    // Ders planlama / randevu (issue #96)
    public DbSet<TeacherAvailabilitySlot> TeacherAvailabilitySlots { get; set; }
    public DbSet<Booking> Bookings { get; set; }

    public DbSet<School> Schools { get; set; }

    // İl / ilçe referans tabloları (issue #91) — ReferenceDataSeed ile doldurulur.
    public DbSet<Province> Provinces { get; set; }
    public DbSet<District> Districts { get; set; }

    // "Soru Çöz" pratik oturumu (issue #62)
    public DbSet<PracticeSession> PracticeSessions { get; set; }
    public DbSet<PracticeSessionQuestion> PracticeSessionQuestions { get; set; }

    // Login denemeleri (issue #84) — BadgeService servis-to-servis yazar, admin dashboard (issue #6) okur.
    public DbSet<LoginEvent> LoginEvents { get; set; }



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

        // Görünürlük eksenleri (issue #9): non-null, mevcut kayıtlar Private + Normal.
        modelBuilder.Entity<Worksheet>()
            .Property(w => w.TeacherSharing)
            .HasDefaultValue(WorksheetTeacherSharing.Private);

        modelBuilder.Entity<Worksheet>()
            .Property(w => w.StudentVisibility)
            .HasDefaultValue(WorksheetStudentVisibility.Normal);

        modelBuilder.Entity<Worksheet>()
            .HasIndex(w => new { w.TeacherSharing, w.StudentVisibility, w.GradeId, w.CreateUserId });

        // Bağımsız öğretmen (issue #92): mevcut tüm öğretmen kayıtları okula bağlı sayılır → Approved.
        // Kolon default'u olmazsa EF CLR default'u (0 = Pending) yazar; bu yüzden açıkça Approved.
        // Sentinel = Approved: aksi halde EF, CLR default'u olan Pending'i "ayarlanmamış" sayıp
        // insert'te DB default'unu (Approved) kullanır ve bağımsız öğretmen Pending başlayamaz.
        modelBuilder.Entity<Teacher>()
            .Property(t => t.ApprovalStatus)
            .HasDefaultValue(TeacherApprovalStatus.Approved)
            .HasSentinel(TeacherApprovalStatus.Approved);

        modelBuilder.Entity<Teacher>()
            .Property(t => t.IsIndependentTutor)
            .HasDefaultValue(false);

        // İl / ilçe referans tabloları + okul adresi (issue #91).
        // Referans kayıtlar silinemez (Restrict) — okul FK'leri nullable, mevcut satırlar etkilenmez.
        modelBuilder.Entity<Province>()
            .HasIndex(p => p.Name)
            .IsUnique();

        modelBuilder.Entity<District>()
            .HasOne(d => d.Province)
            .WithMany(p => p.Districts)
            .HasForeignKey(d => d.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<District>()
            .HasIndex(d => new { d.ProvinceId, d.Name })
            .IsUnique();

        modelBuilder.Entity<School>()
            .HasOne(s => s.Province)
            .WithMany()
            .HasForeignKey(s => s.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<School>()
            .HasOne(s => s.District)
            .WithMany()
            .HasForeignKey(s => s.DistrictId)
            .OnDelete(DeleteBehavior.Restrict);

        // 📌 Grade - Subject İlişkisi
        modelBuilder.Entity<GradeSubject>()
            .HasOne(gs => gs.Grade)
            .WithMany(g => g.GradeSubjects)
            .HasForeignKey(gs => gs.GradeId);

        modelBuilder.Entity<GradeSubject>()
            .HasOne(gs => gs.Subject)
            .WithMany(s => s.GradeSubjects)
            .HasForeignKey(gs => gs.SubjectId);

        // 📌 Teacher - Subject İlişkisi (issue #95: bağımsız öğretmenin verdiği dersler)
        modelBuilder.Entity<TeacherSubject>()
            .HasOne(ts => ts.Teacher)
            .WithMany(t => t.TeacherSubjects)
            .HasForeignKey(ts => ts.TeacherId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TeacherSubject>()
            .HasOne(ts => ts.Subject)
            .WithMany(s => s.TeacherSubjects)
            .HasForeignKey(ts => ts.SubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TeacherSubject>()
            .HasIndex(ts => new { ts.TeacherId, ts.SubjectId })
            .IsUnique();

        // TeacherSubject BaseEntity değil (hard delete); soft-delete edilmiş Teacher/Subject'e bağlı
        // satırlar görünmesin diye uçlardaki global filtrelerle eşleşen filtre tanımlanır (EF 10622 uyarısı).
        modelBuilder.Entity<TeacherSubject>()
            .HasQueryFilter(ts => !ts.Teacher.IsDeleted && !ts.Subject.IsDeleted);

        // Arama sorgusu (IsIndependentTutor && ApprovalStatus == Approved) için bileşik index.
        modelBuilder.Entity<Teacher>()
            .HasIndex(t => new { t.IsIndependentTutor, t.ApprovalStatus });


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

        modelBuilder.Entity<QuestionTransferImportMap>()
            .HasIndex(m => new { m.SourceKey, m.ExternalQuestionKey })
            .IsUnique();

        modelBuilder.Entity<QuestionTransferExportBundle>()
            .HasIndex(b => new { b.SourceKey, b.BundleNo })
            .IsUnique();

        modelBuilder.Entity<QuestionTransferExportMap>()
            .HasIndex(m => new { m.SourceKey, m.QuestionId })
            .IsUnique();

        modelBuilder.Entity<StudyItemImage>()
            .HasOne(i => i.StudyItem)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.StudyItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserProgramStudyPageSchedule>()
            .HasOne(s => s.UserProgram)
            .WithMany(p => p.StudyItemSchedules)
            .HasForeignKey(s => s.UserProgramId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserProgramStudyPageSchedule>()
            .HasOne(s => s.StudyItem)
            .WithMany()
            .HasForeignKey(s => s.StudyItemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WorksheetAssignment>()
            .HasOne(wa => wa.Worksheet)
            .WithMany()
            .HasForeignKey(wa => wa.WorksheetId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorksheetAssignment>()
            .HasOne(wa => wa.Student)
            .WithMany()
            .HasForeignKey(wa => wa.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorksheetAssignment>()
            .HasOne(wa => wa.Grade)
            .WithMany()
            .HasForeignKey(wa => wa.GradeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorksheetAssignment>()
            .HasIndex(wa => new { wa.StudentId, wa.WorksheetId, wa.StartAt });

        modelBuilder.Entity<WorksheetAssignment>()
            .HasIndex(wa => new { wa.GradeId, wa.WorksheetId, wa.StartAt });

        modelBuilder.Entity<WorksheetReminder>()
            .HasOne(wr => wr.Worksheet)
            .WithMany()
            .HasForeignKey(wr => wr.WorksheetId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorksheetReminder>()
            .HasOne(wr => wr.Student)
            .WithMany()
            .HasForeignKey(wr => wr.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorksheetReminder>()
            .HasIndex(wr => new { wr.WorksheetId, wr.StudentId })
            .IsUnique();

        // Atama izni akışı (issue #13)
        modelBuilder.Entity<WorksheetAccessRequest>()
            .HasOne(r => r.Worksheet)
            .WithMany()
            .HasForeignKey(r => r.WorksheetId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tek bekleyen talep: (WorksheetId, RequesterUserId) yalnız Status=Pending iken unique
        // — 409'un DB dayanağı.
        modelBuilder.Entity<WorksheetAccessRequest>()
            .HasIndex(r => new { r.WorksheetId, r.RequesterUserId })
            .IsUnique()
            .HasFilter("\"Status\" = 0");

        modelBuilder.Entity<WorksheetAccessGrant>()
            .HasOne(g => g.Worksheet)
            .WithMany()
            .HasForeignKey(g => g.WorksheetId)
            .OnDelete(DeleteBehavior.Cascade);

        // Aktif tek grant: (WorksheetId, TeacherUserId) yalnız RevokedAt IS NULL iken unique.
        modelBuilder.Entity<WorksheetAccessGrant>()
            .HasIndex(g => new { g.WorksheetId, g.TeacherUserId })
            .IsUnique()
            .HasFilter("\"RevokedAt\" IS NULL");

        // ---- Ders planlama / randevu (issue #96) ----

        modelBuilder.Entity<TeacherAvailabilitySlot>()
            .HasOne(s => s.Teacher)
            .WithMany()
            .HasForeignKey(s => s.TeacherId)
            .OnDelete(DeleteBehavior.Cascade);

        // Öğrencinin "şu öğretmenin gelecekteki boş slotları" sorgusu bu index'i kullanır.
        modelBuilder.Entity<TeacherAvailabilitySlot>()
            .HasIndex(s => new { s.TeacherId, s.Date, s.StartTime });

        // Aynı öğretmen için birebir aynı aralık iki kez tanımlanamaz.
        modelBuilder.Entity<TeacherAvailabilitySlot>()
            .HasIndex(s => new { s.TeacherId, s.Date, s.StartTime, s.EndTime })
            .IsUnique()
            .HasFilter("NOT \"IsDeleted\"");

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.AvailabilitySlot)
            .WithMany(s => s.Bookings)
            .HasForeignKey(b => b.AvailabilitySlotId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Teacher)
            .WithMany()
            .HasForeignKey(b => b.TeacherId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Booking>()
            .HasOne(b => b.Student)
            .WithMany()
            .HasForeignKey(b => b.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Çakışma kontrolünün DB dayanağı: bir slotta yalnızca tek aktif (Pending=0 / Approved=1)
        // booking olabilir. Rejected (2) ve soft-delete edilmiş satırlar index dışıdır.
        modelBuilder.Entity<Booking>()
            .HasIndex(b => b.AvailabilitySlotId)
            .IsUnique()
            .HasFilter("\"Status\" IN (0, 1) AND NOT \"IsDeleted\"");

        modelBuilder.Entity<Booking>()
            .HasIndex(b => new { b.TeacherId, b.Status });

        modelBuilder.Entity<Booking>()
            .HasIndex(b => new { b.StudentId, b.Status });

        // Pratik oturumu (issue #62)
        modelBuilder.Entity<PracticeSession>()
            .HasOne(ps => ps.Student)
            .WithMany()
            .HasForeignKey(ps => ps.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PracticeSession>()
            .HasOne(ps => ps.Grade)
            .WithMany()
            .HasForeignKey(ps => ps.GradeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PracticeSession>()
            .HasIndex(ps => new { ps.StudentId, ps.Status });

        modelBuilder.Entity<PracticeSessionQuestion>()
            .HasOne(pq => pq.PracticeSession)
            .WithMany(ps => ps.Questions)
            .HasForeignKey(pq => pq.PracticeSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PracticeSessionQuestion>()
            .HasOne(pq => pq.Question)
            .WithMany()
            .HasForeignKey(pq => pq.QuestionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PracticeSessionQuestion>()
            .HasOne(pq => pq.SelectedAnswer)
            .WithMany()
            .HasForeignKey(pq => pq.SelectedAnswerId)
            .OnDelete(DeleteBehavior.SetNull);

        // Aynı oturumda aynı soru bir kez gösterilir (no-repeat garantisi DB seviyesinde).
        modelBuilder.Entity<PracticeSessionQuestion>()
            .HasIndex(pq => new { pq.PracticeSessionId, pq.QuestionId })
            .IsUnique();

        // Login event'leri (issue #84). Index'ler ileride admin dashboard'un (issue #6)
        // "kullanıcı bazlı son login" ve "tarih aralığı / rol bazlı sayım" sorguları için.
        modelBuilder.Entity<LoginEvent>()
            .HasIndex(le => new { le.KeycloakUserId, le.OccurredAtUtc });

        modelBuilder.Entity<LoginEvent>()
            .HasIndex(le => new { le.OccurredAtUtc, le.Role, le.Success });

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
