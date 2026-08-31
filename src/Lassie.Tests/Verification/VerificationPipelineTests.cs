using Lassie.Data;
using Lassie.Data.Licenses;
using Lassie.Data.Verification;
using Lassie.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lassie.Tests.Verification;

// Writer- and retention-seam tests: the BackgroundServices are exercised in isolation
// against their own connection to the shared Testcontainer — no WebApplicationFactory, no
// per-test transaction. Rows this class writes are committed for real, so it deletes its
// own License + events on teardown.
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class VerificationPipelineTests(PostgresCollectionFixture fixture) : IAsyncLifetime
{
    private ServiceProvider serviceProvider = null!;
    private LassieDbContext db = null!;
    private long licenseId;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<LassieDbContext>(o => o.UseNpgsql(fixture.ConnectionString));
        serviceProvider = services.BuildServiceProvider();

        db = new LassieDbContext(new DbContextOptionsBuilder<LassieDbContext>()
            .UseNpgsql(fixture.ConnectionString).Options);
        await db.Database.MigrateAsync();

        var license = new License
        {
            Label = $"pipeline-seam-{Guid.NewGuid():N}",
            ApiKeyHash = Guid.NewGuid().ToString("N"),
            IsActive = true,
        };
        db.Licenses.Add(license);
        await db.SaveChangesAsync();
        licenseId = license.Id;
    }

    public async Task DisposeAsync()
    {
        await db.LicenseVerificationEvents.Where(e => e.LicenseId == licenseId).ExecuteDeleteAsync();
        await db.Licenses.Where(l => l.Id == licenseId).ExecuteDeleteAsync();
        await db.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }

    private IServiceScopeFactory ScopeFactory => serviceProvider.GetRequiredService<IServiceScopeFactory>();

    private static IConfiguration EmptyConfig => new ConfigurationBuilder().Build();

    private static IConfiguration ConfigWith(string key, string value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = value })
            .Build();

    private LicenseVerificationEvent NewEvent(DateTimeOffset? occurredAt = null) => new()
    {
        LicenseId = licenseId,
        OccurredAtUtc = occurredAt ?? DateTimeOffset.UtcNow,
        ObservedStatus = LicenseStatus.Active,
    };

    // --- Writer seam ---

    [Fact]
    public async Task DrainOnce_PersistsEveryQueuedEvent()
    {
        var queue = new VerificationEventQueue(EmptyConfig, NullLogger<VerificationEventQueue>.Instance);
        var writer = new VerificationEventWriter(ScopeFactory, queue, NullLogger<VerificationEventWriter>.Instance);

        for (var i = 0; i < 5; i++)
        {
            queue.Enqueue(NewEvent());
        }

        await writer.DrainOnceAsync(CancellationToken.None);

        var rows = await db.LicenseVerificationEvents
            .Where(e => e.LicenseId == licenseId)
            .AsNoTracking()
            .ToListAsync();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r => Assert.Equal(LicenseStatus.Active, r.ObservedStatus));
    }

    [Fact]
    public async Task DrainOnce_SwallowsPersistenceFailure_AndLaterBatchStillPersists()
    {
        var queue = new VerificationEventQueue(EmptyConfig, NullLogger<VerificationEventQueue>.Instance);

        var failingServices = new ServiceCollection();
        failingServices.AddLogging();
        failingServices.AddScoped<LassieDbContext>(_ => new ThrowingDbContext(
            new DbContextOptionsBuilder<LassieDbContext>().UseNpgsql(fixture.ConnectionString).Options));
        await using var failingProvider = failingServices.BuildServiceProvider();

        var failingWriter = new VerificationEventWriter(
            failingProvider.GetRequiredService<IServiceScopeFactory>(),
            queue,
            NullLogger<VerificationEventWriter>.Instance);

        queue.Enqueue(NewEvent());
        await failingWriter.DrainOnceAsync(CancellationToken.None); // must not throw

        var healthyWriter = new VerificationEventWriter(ScopeFactory, queue, NullLogger<VerificationEventWriter>.Instance);
        queue.Enqueue(NewEvent());
        await healthyWriter.DrainOnceAsync(CancellationToken.None);

        var count = await db.LicenseVerificationEvents.Where(e => e.LicenseId == licenseId).CountAsync();
        Assert.Equal(1, count);
    }

    // --- Retention seam ---

    [Fact]
    public async Task Sweep_DeletesRowsOlderThanWindow_KeepsFreshOnes()
    {
        db.LicenseVerificationEvents.AddRange(
            NewEvent(DateTimeOffset.UtcNow.AddDays(-2)),
            NewEvent(DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var service = new VerificationEventRetentionService(
            ScopeFactory,
            ConfigWith("Verification:RetentionDays", "1"),
            NullLogger<VerificationEventRetentionService>.Instance);
        await service.SweepAsync(CancellationToken.None);

        var remaining = await db.LicenseVerificationEvents
            .Where(e => e.LicenseId == licenseId)
            .AsNoTracking()
            .ToListAsync();
        var row = Assert.Single(remaining);
        Assert.True(row.OccurredAtUtc > DateTimeOffset.UtcNow.AddHours(-1));
    }

    [Fact]
    public async Task Sweep_WithRetentionDaysBelowOne_DeletesNothing()
    {
        db.LicenseVerificationEvents.Add(NewEvent(DateTimeOffset.UtcNow.AddDays(-1000)));
        await db.SaveChangesAsync();

        var service = new VerificationEventRetentionService(
            ScopeFactory,
            ConfigWith("Verification:RetentionDays", "0"),
            NullLogger<VerificationEventRetentionService>.Instance);
        await service.SweepAsync(CancellationToken.None);

        var count = await db.LicenseVerificationEvents.Where(e => e.LicenseId == licenseId).CountAsync();
        Assert.Equal(1, count);
    }

    private sealed class ThrowingDbContext(DbContextOptions<LassieDbContext> options) : LassieDbContext(options)
    {
        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated persistence failure");
    }
}
