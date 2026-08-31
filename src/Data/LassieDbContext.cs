using System.Reflection;
using Lassie.Data.Auditing;
using Lassie.Data.Licenses;
using Lassie.Data.Users;
using Microsoft.EntityFrameworkCore;

namespace Lassie.Data;

public class LassieDbContext(DbContextOptions<LassieDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<User> Users => Set<User>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<LicenseVerificationEvent> LicenseVerificationEvents => Set<LicenseVerificationEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditLog>()
            .Property(a => a.Snapshot)
            .HasColumnType("jsonb");

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.EntityName, a.EntityId });

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<License>()
            .HasIndex(l => l.Label)
            .IsUnique();

        modelBuilder.Entity<License>()
            .HasIndex(l => l.ApiKeyHash)
            .IsUnique();

        modelBuilder.Entity<LicenseVerificationEvent>()
            .HasOne(e => e.License)
            .WithMany()
            .HasForeignKey(e => e.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);

        // Serves the per-license, newest-first history query.
        modelBuilder.Entity<LicenseVerificationEvent>()
            .HasIndex(e => new { e.LicenseId, e.OccurredAtUtc });

        // Serves the retention sweep's `WHERE OccurredAtUtc < cutoff`.
        modelBuilder.Entity<LicenseVerificationEvent>()
            .HasIndex(e => e.OccurredAtUtc);
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
            var entityType = entry.Entity.GetType();
            var snapshotValues = new Dictionary<string, object?>();
            foreach (var property in entry.OriginalValues.Properties)
            {
                var clrProperty = entityType.GetProperty(property.Name);
                if (clrProperty?.GetCustomAttribute<NotAuditedAttribute>() is not null)
                {
                    continue;
                }

                snapshotValues[property.Name] = entry.OriginalValues[property];
            }

            var snapshot = System.Text.Json.JsonSerializer.Serialize(snapshotValues);
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
