using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lassie.Data.Verification;

// Periodically prunes verification-audit rows past the retention window, keeping the table
// and the stored caller-IP data bounded. Never an admin action — the panel view is
// append-only from the user's side.
public sealed class VerificationEventRetentionService(
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<VerificationEventRetentionService> log) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = config.GetValue("Verification:RetentionSweepInterval", TimeSpan.FromHours(6));

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            using var timer = new PeriodicTimer(interval);
            do
            {
                await SweepAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutting down.
        }
    }

    // internal for the retention-seam test (InternalsVisibleTo Lassie.Tests).
    internal async Task SweepAsync(CancellationToken ct)
    {
        try
        {
            var days = config.GetValue("Verification:RetentionDays", 90);
            if (days < 1)
            {
                log.LogWarning(
                    "Verification:RetentionDays is {Days} (< 1); skipping retention sweep so a misconfigured value can't wipe the whole table.",
                    days);
                return;
            }

            var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(days);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LassieDbContext>();
            var deleted = await db.LicenseVerificationEvents
                .Where(e => e.OccurredAtUtc < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
            {
                log.LogInformation(
                    "Retention sweep deleted {Deleted} verification audit event(s) older than {Days} day(s).",
                    deleted, days);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Verification retention sweep failed; will retry on the next interval.");
        }
    }
}
