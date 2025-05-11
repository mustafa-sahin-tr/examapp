using Microsoft.EntityFrameworkCore;
using OutboxPublisherService.Models;

namespace OutboxPublisherService.Data;

public class AppDbContext : DbContext
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
