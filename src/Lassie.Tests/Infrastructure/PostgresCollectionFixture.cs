using Testcontainers.PostgreSql;
using Xunit;

namespace Lassie.Tests.Infrastructure;

public class PostgresCollectionFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresCollectionFixture>;
