using System.Text.Json;
using Lassie.Data.Licenses;
using Lassie.Tests.Infrastructure;
using Xunit;

namespace Lassie.Tests.Licenses;

[Collection("Postgres")]
public class VerifyEndpointKeySecrecyTests(PostgresCollectionFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ValidKey_ResponseBodyHasOnlyValidProperty()
    {
        var (rawKey, hash) = ApiKeyHasher.Generate();
        DbContext.Licenses.Add(new License
        {
            Label = "secrecy-test-valid-key",
            ApiKeyHash = hash,
            IsActive = true
        });
        await DbContext.SaveChangesAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/license/verify");
        request.Headers.Add("X-Api-Key", rawKey);
        var response = await HttpClient.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var properties = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["valid"], properties);
    }

    [Fact]
    public async Task InvalidKey_ResponseBodyCarriesNoJson()
    {
        // No matching license, so the endpoint short-circuits to Results.Unauthorized() —
        // an empty 401 body, which is a strictly stronger secrecy guarantee than a JSON
        // body with a filtered property set would be: there is nothing to leak from at all.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/license/verify");
        request.Headers.Add("X-Api-Key", "garbage-key-that-does-not-exist");
        var response = await HttpClient.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Empty(body);
    }
}
