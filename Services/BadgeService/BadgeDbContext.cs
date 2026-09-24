using System;
using BadgeService.Entities;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BadgeService;

public class BadgeDbContext : DbContext
{
    public BadgeDbContext(DbContextOptions<BadgeDbContext> options) : base(options) { }

    public DbSet<BadgeDefinition> BadgeDefinitions => Set<BadgeDefinition>();
    public DbSet<BadgeEarned> BadgeEarned => Set<BadgeEarned>();
    public DbSet<StudentQuestionAggregate> StudentQuestionAggregates => Set<StudentQuestionAggregate>();
    public DbSet<StudentSubjectAggregate> StudentSubjectAggregates => Set<StudentSubjectAggregate>();
    public DbSet<StudentDailyActivity> StudentDailyActivities => Set<StudentDailyActivity>();
    public DbSet<StudentBadgeProgress> StudentBadgeProgresses => Set<StudentBadgeProgress>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ProcessedLoginAttempt> ProcessedLoginAttempts => Set<ProcessedLoginAttempt>();
    public DbSet<ProcessedAnswerSubmission> ProcessedAnswerSubmissions => Set<ProcessedAnswerSubmission>();
    public DbSet<AnswerPointAward> AnswerPointAwards => Set<AnswerPointAward>();
    public DbSet<UserLocalePreference> UserLocalePreferences => Set<UserLocalePreference>();

    /// <summary>
    /// BadgeService'in kendi transactional outbox'ı (issue #225). exam/identity DB'lerindeki tabloyla
    /// aynı şema (<see cref="OutboxMessage"/>, tablo adı "OutboxMessages") — böylece aynı
    /// OutboxPublisher kodu üçüncü bir instance (<c>badge-outbox-publisher</c>) olarak bu DB'yi poll eder.
    /// </summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BadgeDefinition>().HasKey(x => x.Id);
        modelBuilder.Entity<BadgeEarned>().HasKey(x => x.Id);

        modelBuilder.Entity<BadgeEarned>()
            .HasOne(x => x.BadgeDefinition)
            .WithMany()
            .HasForeignKey(x => x.BadgeDefinitionId);

        modelBuilder.Entity<StudentQuestionAggregate>().HasKey(x => x.Id);
        modelBuilder.Entity<StudentQuestionAggregate>()
            .HasIndex(x => x.UserId)
            .IsUnique();

        modelBuilder.Entity<StudentSubjectAggregate>().HasKey(x => x.Id);
        modelBuilder.Entity<StudentSubjectAggregate>()
            .HasIndex(x => new { x.UserId, x.SubjectId })
            .IsUnique();

        modelBuilder.Entity<StudentDailyActivity>().HasKey(x => x.Id);
        modelBuilder.Entity<StudentDailyActivity>()
            .HasIndex(x => new { x.UserId, x.ActivityDate })
            .IsUnique();

        // issue #279 (item 1, yatay ölçekleme): birden fazla BadgeService instance'ı aynı aggregate satırını
        // eşzamanlı güncelleyebilir (AnswerSubmittedConsumerDefinition'ın partitioner'ı yalnızca TEK instance
        // içinde UserId'ye göre sıralama garantisi verir). Postgres'in sistem sütunu xmin'i concurrency token
        // olarak kullanmak, ayrı bir versiyon kolonu eklemeden "kaybolan güncelleme"yi DbUpdateConcurrencyException
        // olarak yüzeye çıkarır; AnswerSubmissionAggregationService bunu ConcurrencyRetry ile sınırlı sayıda dener.
        // Yalnızca Npgsql'de uygulanır: xmin gerçek bir Postgres sistem sütunudur, test sağlayıcısı (sqlite,
        // BadgeTestDb) bunu tanımaz — concurrency retry yolu bu yüzden testlerde sahte bir DbUpdateConcurrencyException
        // ile (ConcurrencyRetry üzerinden, DB'den bağımsız) doğrulanır.
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<StudentQuestionAggregate>().Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
            modelBuilder.Entity<StudentSubjectAggregate>().Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
            modelBuilder.Entity<StudentDailyActivity>().Property<uint>("xmin").HasColumnName("xmin").IsRowVersion();
        }

        modelBuilder.Entity<StudentBadgeProgress>().HasKey(x => x.Id);
        modelBuilder.Entity<StudentBadgeProgress>()
            .HasOne(x => x.BadgeDefinition)
            .WithMany()
            .HasForeignKey(x => x.BadgeDefinitionId);
        modelBuilder.Entity<StudentBadgeProgress>()
            .HasIndex(x => new { x.UserId, x.BadgeDefinitionId })
            .IsUnique();

        modelBuilder.Entity<Notification>().HasKey(x => x.Id);
        modelBuilder.Entity<Notification>()
            .HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
        modelBuilder.Entity<Notification>()
            .HasIndex(x => new { x.UserKeycloakId, x.IsRead, x.CreatedAt });
        // Idempotency: bir reminder tetiklemesi en fazla bir bildirim üretir.
        modelBuilder.Entity<Notification>()
            .HasIndex(x => new { x.Type, x.SourceReminderId })
            .IsUnique()
            .HasFilter("\"SourceReminderId\" IS NOT NULL");
        // Idempotency: bir atama izni talebi/kararı, tipi başına en fazla bir bildirim üretir.
        modelBuilder.Entity<Notification>()
            .HasIndex(x => new { x.Type, x.SourceAccessRequestId })
            .IsUnique()
            .HasFilter("\"SourceAccessRequestId\" IS NOT NULL");
        // Idempotency: bir bağımsız öğretmen BAŞVURUSU (issue #94, "TeacherApplicationSubmitted") tipi
        // başına en fazla bir bildirim üretir. Filtre bilinçli olarak Type'a da bağlanır (issue #157
        // review): aynı sütun (SourceTeacherApplicationId) karar bildirimlerinde (TeacherApplicationApproved/
        // Rejected) REFERANS amaçlı dolu olabilir ama aynı öğretmen zaman içinde birden fazla karara konu
        // olabildiğinden (ör. red sonrası yeni okul talebi) o tipler için burada tekillik KURULMAZ —
        // dedup'ları SourceEventId üzerinden yapılır (aşağıda).
        modelBuilder.Entity<Notification>()
            .HasIndex(x => new { x.Type, x.SourceTeacherApplicationId })
            .IsUnique()
            .HasFilter("\"SourceTeacherApplicationId\" IS NOT NULL AND \"Type\" = 'TeacherApplicationSubmitted'");
        // Idempotency: öğretmen başvurusu KARARI (issue #157) — TeacherId tek başına tekillik için yetersiz
        // (aynı öğretmen birden fazla kez karara konu olabilir); dedup event'in kendi Guid kimliğiyle yapılır.
        modelBuilder.Entity<Notification>()
            .HasIndex(x => new { x.Type, x.SourceEventId })
            .IsUnique()
            .HasFilter("\"SourceEventId\" IS NOT NULL");

        modelBuilder.Entity<ProcessedLoginAttempt>().HasKey(x => x.Id);
        // Idempotency: aynı login denemesi (event'in kendi EventId'si) en fazla bir kez exam API'ye yazılır.
        modelBuilder.Entity<ProcessedLoginAttempt>()
            .HasIndex(x => x.EventId)
            .IsUnique();

        // Idempotency: aynı AnswerSubmittedEvent (EventId = outbox satırının Id'si, issue #243) en
        // fazla bir kez aggregate'e uygulanır. EventId doğrudan PK — aggregate güncellemesiyle AYNI
        // SaveChanges'te eklenir (bkz. AnswerSubmissionAggregationService), tekrar teslimde PK/unique
        // ihlali (23505) no-op olarak ele alınır.
        modelBuilder.Entity<ProcessedAnswerSubmission>().HasKey(x => x.EventId);
        modelBuilder.Entity<ProcessedAnswerSubmission>()
            .Property(x => x.EventId)
            .ValueGeneratedNever();
        // issue #279 (item 3): öğrenci reset/KVKK silmesi (UserId = X, bkz. UserResetService) bu index'i
        // kullanır — PK EventId'ye göre olduğundan aksi halde tam tablo taraması olurdu.
        modelBuilder.Entity<ProcessedAnswerSubmission>()
            .HasIndex(x => x.UserId);
        // issue #279 review (item 5, yorum düzeltmesi): retention taraması (ProcessedAt < cutoff,
        // ProcessedAnswerSubmissionRetentionJob) UserId değil BU index'i kullanır — yukarıdaki UserId
        // index'i retention için işe yaramaz (WHERE/ORDER BY ProcessedAt'a göre).
        modelBuilder.Entity<ProcessedAnswerSubmission>()
            .HasIndex(x => x.ProcessedAt);

        // issue #279 (item 4): "soru başına bir kez, son cevap sayılır" — bkz. AnswerPointAward XML doc.
        modelBuilder.Entity<AnswerPointAward>()
            .HasKey(x => new { x.TestInstanceId, x.QuestionId });
        modelBuilder.Entity<AnswerPointAward>()
            .HasIndex(x => x.UserId);

        // UserId doğal PK: upsert "var mı" kontrolüne gerek bırakmadan tek satır garantiler.
        // ValueGeneratedNever ŞART — UserId auth-api'den (dış kaynak) geliyor, EF'in kendi
        // identity sequence'ı ile üretilmemeli (aksi halde consumer'ın atadığı değer sessizce
        // görmezden gelinir).
        modelBuilder.Entity<UserLocalePreference>().HasKey(x => x.UserId);
        modelBuilder.Entity<UserLocalePreference>()
            .Property(x => x.UserId)
            .ValueGeneratedNever();
        modelBuilder.Entity<UserLocalePreference>()
            .HasIndex(x => x.KeycloakId);
        modelBuilder.Entity<UserLocalePreference>()
            .Property(x => x.Locale)
            .HasMaxLength(8);
    }
}
