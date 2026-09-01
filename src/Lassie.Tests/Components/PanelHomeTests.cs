using Bunit;
using Lassie.Components;
using Lassie.Components.Pages;
using Lassie.Data;
using Lassie.Data.Licenses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace Lassie.Tests.Components;

public class PanelHomeTests : BunitContext, IAsyncLifetime
{
    private readonly LassieDbContext dbContext;

    public PanelHomeTests()
    {
        Services.AddMudServices();
        Services.AddScoped<ThemeState>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        dbContext = new LassieDbContext(new DbContextOptionsBuilder<LassieDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        Services.AddSingleton(dbContext);
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await ((IAsyncDisposable)this).DisposeAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public void Grid_ShowsMostRecentVerificationTimestamp()
    {
        var license = new License { Id = 1, Label = "verified-license", ApiKeyHash = "hash-1", IsActive = true };
        dbContext.Licenses.Add(license);
        dbContext.LicenseVerificationEvents.AddRange(
            new LicenseVerificationEvent { LicenseId = 1, OccurredAtUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), ObservedStatus = LicenseStatus.Active },
            new LicenseVerificationEvent { LicenseId = 1, OccurredAtUtc = new DateTimeOffset(2026, 1, 3, 10, 0, 0, TimeSpan.Zero), ObservedStatus = LicenseStatus.Active });
        dbContext.SaveChanges();

        var cut = Render<PanelHome>();

        cut.WaitForAssertion(() => Assert.Contains("2026-01-03 10:00 UTC", cut.Markup));
        Assert.DoesNotContain("2026-01-01", cut.Markup);
    }

    [Fact]
    public void Grid_ShowsNeverForLicenseWithNoVerifications()
    {
        dbContext.Licenses.Add(new License { Id = 1, Label = "unverified-license", ApiKeyHash = "hash-1", IsActive = true });
        dbContext.SaveChanges();

        var cut = Render<PanelHome>();

        cut.WaitForAssertion(() => Assert.Contains("Never", cut.Markup));
    }
}
