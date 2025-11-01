using System;
using BadgeService.Entities;
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

        modelBuilder.Entity<StudentBadgeProgress>().HasKey(x => x.Id);
        modelBuilder.Entity<StudentBadgeProgress>()
            .HasOne(x => x.BadgeDefinition)
            .WithMany()
            .HasForeignKey(x => x.BadgeDefinitionId);
        modelBuilder.Entity<StudentBadgeProgress>()
            .HasIndex(x => new { x.UserId, x.BadgeDefinitionId })
            .IsUnique();
    }
}
