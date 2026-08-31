using System.Net;
using System.Threading.Channels;
using Lassie.Data.Licenses;
using Lassie.Data.Verification;
using Lassie.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lassie.Tests.Licenses;

// Enqueue seam: the verify endpoint's audit side-effect is exercised end-to-end through the
// HTTP pipeline, but with the real queue swapped for an inspectable fake — so no
// BackgroundService / DB write is involved and the transactional fixture stays intact.
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class VerifyEndpointAuditEnqueueTests(PostgresCollectionFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed class CapturingQueue : IVerificationEventQueue
    {
        public List<LicenseVerificationEvent> Events { get; } = [];
        public bool ThrowOnEnqueue { get; set; }

        public ChannelReader<LicenseVerificationEvent> Reader { get; } =
            Channel.CreateUnbounded<LicenseVerificationEvent>().Reader;

        public void Enqueue(LicenseVerificationEvent evt)
        {
            if (ThrowOnEnqueue)
            {
                throw new InvalidOperationException("simulated enqueue failure");
            }

            Events.Add(evt);
        }
    }

    private readonly CapturingQueue _queue = new();

    protected override void ConfigureTestServices(IServiceCollection services) =>
        services.AddSingleton<IVerificationEventQueue>(_queue);

    private async Task<string> SeedLicenseAsync(string label, bool isActive = true, DateOnly? expiresOn = null)
    {
        var (rawKey, hash) = ApiKeyHasher.Generate();
        DbContext.Licenses.Add(new License
        {
            Label = label,
            ApiKeyHash = hash,
            IsActive = isActive,
            ExpiresOn = expiresOn,
        });
        await DbContext.SaveChangesAsync();
        return rawKey;
    }

    private async Task<HttpResponseMessage> VerifyAsync(string? apiKey, string? forwardedFor = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/license/verify");
        if (apiKey is not null)
        {
            request.Headers.Add("X-Api-Key", apiKey);
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return await HttpClient.SendAsync(request);
    }

    [Fact]
    public async Task ResolvedActiveLicense_EnqueuesOneEvent()
    {
        var rawKey = await SeedLicenseAsync("enqueue-active");
        var license = DbContext.Licenses.Single(l => l.Label == "enqueue-active");

        var response = await VerifyAsync(rawKey);

        response.EnsureSuccessStatusCode();
        var evt = Assert.Single(_queue.Events);
        Assert.Equal(license.Id, evt.LicenseId);
        Assert.Equal(LicenseStatus.Active, evt.ObservedStatus);
        Assert.True(evt.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task ResolvedExpiredLicense_EnqueuesEventWithExpiredStatus()
    {
        var rawKey = await SeedLicenseAsync(
            "enqueue-expired",
            expiresOn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1));

        await VerifyAsync(rawKey);

        var evt = Assert.Single(_queue.Events);
        Assert.Equal(LicenseStatus.Expired, evt.ObservedStatus);
    }

    [Fact]
    public async Task ForwardedForHeader_IsCapturedOnTheEvent()
    {
        var rawKey = await SeedLicenseAsync("enqueue-xff");

        await VerifyAsync(rawKey, forwardedFor: "203.0.113.7");

        var evt = Assert.Single(_queue.Events);
        Assert.Equal("203.0.113.7", evt.ClientIp);
    }

    [Fact]
    public async Task NoForwardedForHeader_ClientIpIsNull()
    {
        var rawKey = await SeedLicenseAsync("enqueue-no-xff");

        await VerifyAsync(rawKey);

        var evt = Assert.Single(_queue.Events);
        Assert.Null(evt.ClientIp);
    }

    [Fact]
    public async Task MissingApiKey_EnqueuesNothing()
    {
        var response = await VerifyAsync(apiKey: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_queue.Events);
    }

    [Fact]
    public async Task UnknownApiKey_EnqueuesNothing()
    {
        var response = await VerifyAsync("no-such-key");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_queue.Events);
    }

    [Fact]
    public async Task QueueEnqueueThrows_VerifyStillReturns200()
    {
        var rawKey = await SeedLicenseAsync("enqueue-throws");
        _queue.ThrowOnEnqueue = true;

        var response = await VerifyAsync(rawKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"valid\":true}", await response.Content.ReadAsStringAsync());
    }
}
