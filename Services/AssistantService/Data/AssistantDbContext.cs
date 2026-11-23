using Microsoft.EntityFrameworkCore;

namespace AssistantService.Data;

public sealed class AssistantDbContext(DbContextOptions<AssistantDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<PendingAction> PendingActions => Set<PendingAction>();
    public DbSet<UsageDay> Usage => Set<UsageDay>();
    public DbSet<Feedback> Feedback => Set<Feedback>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Conversation>(e =>
        {
            e.Property(c => c.UserSub).HasMaxLength(64);
            e.Property(c => c.Role).HasMaxLength(32);
            e.Property(c => c.Title).HasMaxLength(200);
            e.HasIndex(c => new { c.UserSub, c.UpdatedAt });
            // Retention purge (#505) scans by age.
            e.HasIndex(c => c.UpdatedAt);
            e.HasMany(c => c.Messages).WithOne(m => m.Conversation).HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<Message>(e =>
        {
            e.Property(m => m.Role).HasMaxLength(16);
            e.Property(m => m.Model).HasMaxLength(64);
            e.Property(m => m.CostUsd).HasPrecision(12, 6);
            e.HasIndex(m => new { m.ConversationId, m.Id });
        });
        modelBuilder.Entity<PendingAction>(e =>
        {
            e.Property(p => p.UserSub).HasMaxLength(64);
            e.Property(p => p.Tool).HasMaxLength(100);
            e.HasIndex(p => new { p.UserSub, p.Status });
            e.HasOne<Conversation>().WithMany().HasForeignKey(p => p.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UsageDay>(e =>
        {
            e.ToTable("Usage");
            e.Property(u => u.UserSub).HasMaxLength(64);
            e.Property(u => u.CostUsd).HasPrecision(12, 6);
            e.HasIndex(u => new { u.UserSub, u.Day }).IsUnique();
            e.HasIndex(u => u.Day);
        });
        modelBuilder.Entity<Feedback>(e =>
        {
            e.Property(f => f.UserSub).HasMaxLength(64);
            e.Property(f => f.Reason).HasMaxLength(32);
            e.HasIndex(f => new { f.MessageId, f.UserSub }).IsUnique();
            e.HasOne<Message>().WithMany().HasForeignKey(f => f.MessageId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
