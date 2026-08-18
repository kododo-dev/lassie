using System.Text.Json;
using Lassie.Data.Auditing;
using Lassie.Data.Licenses;
using Lassie.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lassie.Tests.Auditing;

[Collection("Postgres")]
[Trait("Category", "Integration")]
public class AuditLoadBeforeMutateTests(PostgresCollectionFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EditingLabel_AuditSnapshotRecordsTruePriorLabel()
    {
        const string originalLabel = "audit-original-label";

        var license = new License { Label = originalLabel, ApiKeyHash = "irrelevant-hash-1", IsActive = true };
        DbContext.Licenses.Add(license);
        await DbContext.SaveChangesAsync();

        // Mirrors EditLicense.razor:86,146-159 — load fresh, mutate the tracked instance,
        // save. This is what makes entry.OriginalValues reflect the true prior row.
        var loaded = await DbContext.Licenses.SingleAsync(l => l.Id == license.Id);
        loaded.Label = "audit-mutated-label";
        await DbContext.SaveChangesAsync();

        var auditLog = await DbContext.AuditLogs.SingleAsync();

        Assert.Equal(nameof(License), auditLog.EntityName);
        Assert.Equal(license.Id.ToString(), auditLog.EntityId);
        Assert.Equal(AuditChangeType.Modified, auditLog.ChangeType);

        using var snapshot = JsonDocument.Parse(auditLog.Snapshot);
        Assert.Equal(originalLabel, snapshot.RootElement.GetProperty("Label").GetString());
    }

    [Fact]
    public async Task DeactivatingLicense_AuditSnapshotRecordsTruePriorIsActive()
    {
        var license = new License { Label = "audit-deactivate-label", ApiKeyHash = "irrelevant-hash-2", IsActive = true };
        DbContext.Licenses.Add(license);
        await DbContext.SaveChangesAsync();

        var loaded = await DbContext.Licenses.SingleAsync(l => l.Id == license.Id);
        loaded.IsActive = false;
        await DbContext.SaveChangesAsync();

        var auditLog = await DbContext.AuditLogs.SingleAsync();

        Assert.Equal(nameof(License), auditLog.EntityName);
        Assert.Equal(license.Id.ToString(), auditLog.EntityId);
        Assert.Equal(AuditChangeType.Modified, auditLog.ChangeType);

        using var snapshot = JsonDocument.Parse(auditLog.Snapshot);
        Assert.True(snapshot.RootElement.GetProperty("IsActive").GetBoolean());
    }
}
