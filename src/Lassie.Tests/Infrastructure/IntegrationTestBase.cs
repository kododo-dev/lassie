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
        await DbContext.DisposeAsync();
        await _transaction.RollbackAsync();
        await _transaction.DisposeAsync();
        await Factory.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
