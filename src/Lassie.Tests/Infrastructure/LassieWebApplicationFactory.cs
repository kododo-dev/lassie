using Lassie.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Lassie.Tests.Infrastructure;

// The test transaction doesn't exist yet when the factory (and therefore the app host,
// including its Migrate() call) is built — see IntegrationTestBase's ordering. This holder
// lets the transaction be attached afterwards while still being visible to every
// request-scoped LassieDbContext the running host creates later.
public class TransactionHolder
{
    public NpgsqlTransaction? Transaction { get; set; }
}

public class LassieWebApplicationFactory(
    NpgsqlConnection connection,
    TransactionHolder transactionHolder,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ADMIN_EMAIL", "admin@test.local");
        builder.UseSetting("ADMIN_PASSWORD", "test-password-not-used");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<LassieDbContext>>();
            services.AddDbContext<LassieDbContext>(options => options.UseNpgsql(connection));

            // The verification-audit BackgroundServices resolve their own DI scope on the
            // shared transactional connection, off the request pipeline and on a background
            // thread — incompatible with this fixture's per-request transaction enlistment
            // and Npgsql's single-command-per-connection rule. That pipeline is exercised in
            // isolation instead (VerificationPipelineTests / VerifyEndpointAuditEnqueueTests).
            services.RemoveAll<IHostedService>();

            services.AddSingleton<IStartupFilter>(new EnlistTransactionStartupFilter(transactionHolder));

            // Per-test service overrides (last registration wins for GetRequiredService).
            configureServices?.Invoke(services);
        });
    }

    // Runs first in the pipeline so every request's LassieDbContext joins the test's
    // outer transaction before any endpoint code touches it (per EF Core's documented
    // "sharing transactions across context instances" pattern).
    private class EnlistTransactionStartupFilter(TransactionHolder transactionHolder) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    if (transactionHolder.Transaction is not null)
                    {
                        var dbContext = context.RequestServices.GetRequiredService<LassieDbContext>();
                        await dbContext.Database.UseTransactionAsync(transactionHolder.Transaction);
                    }

                    await nextMiddleware();
                });
                next(app);
            };
    }
}
