using Lassie.Data.Licenses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lassie.Data.Verification;

// Drains the verification-audit queue and batch-inserts rows on its own DI scope. A
// persistence failure is logged and the batch is dropped — losing an audit row must never
// take down the host or bleed back into the verify response path. This deliberately trades
// away the same-transaction atomicity of the generic AuditLog mechanism.
public sealed class VerificationEventWriter(
    IServiceScopeFactory scopeFactory,
    IVerificationEventQueue queue,
    ILogger<VerificationEventWriter> log) : BackgroundService
{
    private const int MaxBatchSize = 200;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken))
            {
                await DrainOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — StopAsync flushes whatever is still buffered.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // base.StopAsync signals the stopping token and waits for ExecuteAsync to exit, so
        // the drain below is the only reader by the time it runs (SingleReader holds).
        await base.StopAsync(cancellationToken);

        while (queue.Reader.Count > 0)
        {
            await DrainOnceAsync(cancellationToken);
        }
    }

    // internal for the writer-seam test (InternalsVisibleTo Lassie.Tests): reads up to one
    // batch and persists it, swallowing any persistence error.
    internal async Task DrainOnceAsync(CancellationToken ct)
    {
        var batch = new List<LicenseVerificationEvent>(MaxBatchSize);
        while (batch.Count < MaxBatchSize && queue.Reader.TryRead(out var evt))
        {
            batch.Add(evt);
        }

        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LassieDbContext>();
            db.LicenseVerificationEvents.AddRange(batch);
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-batch; drop quietly.
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to persist {Count} verification audit event(s); batch dropped.", batch.Count);
        }
    }
}
