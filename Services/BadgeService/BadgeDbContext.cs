using System;
using Microsoft.EntityFrameworkCore;

namespace BadgeService;

public class BadgeDbContext : DbContext
{
    public BadgeDbContext(DbContextOptions<BadgeDbContext> options) : base(options) { }

    public DbSet<BadgeDefinition> BadgeDefinitions => Set<BadgeDefinition>();
    public DbSet<BadgeEarned> BadgeEarned => Set<BadgeEarned>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BadgeDefinition>().HasKey(x => x.Id);
        modelBuilder.Entity<BadgeEarned>().HasKey(x => x.Id);

        modelBuilder.Entity<BadgeEarned>()
            .HasOne(x => x.BadgeDefinition)
            .WithMany()
            .HasForeignKey(x => x.BadgeDefinitionId);
    }
}
