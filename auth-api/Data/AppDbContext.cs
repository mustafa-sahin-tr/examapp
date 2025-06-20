using System;
using System.Linq.Expressions;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Data;
using System.ComponentModel.DataAnnotations;

public abstract class BaseEntity
    {
        public DateTime CreateTime { get; set; }        
        public int? CreateUserId { get; set; }
        public DateTime? UpdateTime { get; set; }
        public int? UpdateUserId { get; set; }
        public DateTime? DeleteTime { get; set; }
        public int? DeleteUserId { get; set; }
        public bool IsDeleted { get; set; } = false;
    }

    public class User : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [MaxLength(50)]
    public string KeycloakId { get; set; } = null!;
    
    [Required, MaxLength(100)]
    public string FullName { get; set; }

    [Required, MaxLength(100)]
    public string Email { get; set; }
    
    public string? PasswordHash { get; set; }

    public string? AvatarUrl { get; set; }

    [Required]
    public string Role { get; set; } // Öğrenci, Öğretmen, Veli
 
}

public enum UserRole
{
    Student,
    Teacher,
    Parent
}

public class AppDbContext : DbContext
{
    private int? _currentUserId = 0;  // Varsayılan olarak 0
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
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
    


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        

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