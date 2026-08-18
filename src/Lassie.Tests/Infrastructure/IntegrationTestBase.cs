using Lassie.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Lassie.Tests.Infrastructure;

public abstract class IntegrationTestBase(PostgresCollectionFixture fixture) : IAsyncLifetime
{
    private NpgsqlConnection _connection = null!;
    private NpgsqlTransaction _transaction = null!;

    protected LassieWebApplicationFactory Factory { get; private set; } = null!;
    protected HttpClient HttpClient { get; private set; } = null!;
    protected LassieDbContext DbContext { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _connection = new NpgsqlConnection(fixture.ConnectionString);
        await _connection.OpenAsync();

        var transactionHolder = new TransactionHolder();
        Factory = new LassieWebApplicationFactory(_connection, transactionHolder);

        // Forces host startup (Migrate() + admin seed) while the connection is still
        // transaction-free, so the app's own migration transactions never nest inside ours.
        // Consequence: that seed is a real, non-rolled-back commit against the
        // collection-shared container — the first test in a run permanently seeds an
        // admin User row that every later test in the same run will also see.
        HttpClient = Factory.CreateClient();

        _transaction = await _connection.BeginTransactionAsync();
        transactionHolder.Transaction = _transaction;

        var options = new DbContextOptionsBuilder<LassieDbContext>()
            .UseNpgsql(_connection)
            .Options;
        DbContext = new LassieDbContext(options);
        await DbContext.Database.UseTransactionAsync(_transaction);
    }

    public async Task DisposeAsync()
    {
        // Guarded because InitializeAsync assigns these fields one at a time across
        // several fallible awaits — a failure partway through would otherwise leave
        // later fields null and NRE here, masking the real error and skipping cleanup
        // of whatever was already acquired.
        if (DbContext is not null)
        {
            await DbContext.DisposeAsync();
        }

        if (_transaction is not null)
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
        }

        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
