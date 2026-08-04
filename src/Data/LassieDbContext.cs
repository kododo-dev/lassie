using Lassie.Data.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Lassie.Data;

public class LassieDbContext(DbContextOptions<LassieDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.Snapshot)
            .HasColumnType("jsonb");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddAuditLogEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AddAuditLogEntries();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    // Runs before the base save call so the AuditLog rows it adds to the ChangeTracker
    // are included in the same transaction as the changes they're recording.
    private void AddAuditLogEntries()
    {
        var auditableEntries = ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable && e.State is EntityState.Modified or EntityState.Deleted);

        foreach (var entry in auditableEntries)
        {
            var snapshot = System.Text.Json.JsonSerializer.Serialize(entry.OriginalValues.ToObject());
            var primaryKey = entry.Properties.First(p => p.Metadata.IsPrimaryKey()).CurrentValue;

            AuditLogs.Add(new AuditLog
            {
                EntityName = entry.Entity.GetType().Name,
                EntityId = primaryKey?.ToString() ?? string.Empty,
                ChangeType = entry.State == EntityState.Deleted ? AuditChangeType.Deleted : AuditChangeType.Modified,
                ChangedAtUtc = DateTimeOffset.UtcNow,
                Snapshot = snapshot
            });
        }
    }
}
