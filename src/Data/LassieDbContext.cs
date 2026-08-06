using Lassie.Data.Auditing;
using Lassie.Data.LicenseFields;
using Lassie.Data.Users;
using Microsoft.EntityFrameworkCore;

namespace Lassie.Data;

public class LassieDbContext(DbContextOptions<LassieDbContext> options) : DbContext(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<User> Users => Set<User>();
    public DbSet<LicenseField> LicenseFields => Set<LicenseField>();
    public DbSet<LicenseFieldOption> LicenseFieldOptions => Set<LicenseFieldOption>();

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

        modelBuilder.Entity<LicenseField>()
            .HasIndex(f => f.Name)
            .IsUnique();

        modelBuilder.Entity<LicenseFieldOption>()
            .HasIndex(o => new { o.LicenseFieldId, o.Value })
            .IsUnique();

        modelBuilder.Entity<LicenseField>()
            .HasMany(f => f.Options)
            .WithOne()
            .HasForeignKey(o => o.LicenseFieldId)
            .OnDelete(DeleteBehavior.Cascade);
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
