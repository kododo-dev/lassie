using System.Net;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lassie.Tests.Infrastructure;

[Collection("Postgres")]
public class FixtureSmokeTests(PostgresCollectionFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Fixture_StartsContainerMigratesAndRespondsToHttp()
    {
        Assert.Empty(await DbContext.Licenses.ToListAsync());
        Assert.Empty(await DbContext.AuditLogs.ToListAsync());

        var response = await HttpClient.GetAsync("/api/license/verify");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
