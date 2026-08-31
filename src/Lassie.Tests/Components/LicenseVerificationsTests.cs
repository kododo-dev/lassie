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

public class LicenseVerificationsTests : BunitContext, IAsyncLifetime
{
    private readonly LassieDbContext dbContext;

    public LicenseVerificationsTests()
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
    public void UnknownId_RendersNotFoundAlert()
    {
        var cut = Render<LicenseVerifications>(p => p.Add(x => x.Id, 999));

        Assert.Contains("License not found", cut.Markup);
    }

    [Fact]
    public void Grid_RendersEventsNewestFirst()
    {
        var license = new License { Id = 1, Label = "history-grid", ApiKeyHash = "hash-1", IsActive = true };
        dbContext.Licenses.Add(license);
        dbContext.LicenseVerificationEvents.AddRange(
            new LicenseVerificationEvent { LicenseId = 1, OccurredAtUtc = new DateTimeOffset(2026, 1, 3, 10, 0, 0, TimeSpan.Zero), ObservedStatus = LicenseStatus.Active },
            new LicenseVerificationEvent { LicenseId = 1, OccurredAtUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero), ObservedStatus = LicenseStatus.Active },
            new LicenseVerificationEvent { LicenseId = 1, OccurredAtUtc = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero), ObservedStatus = LicenseStatus.Active });
        dbContext.SaveChanges();

        var cut = Render<LicenseVerifications>(p => p.Add(x => x.Id, 1));

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            var newest = markup.IndexOf("2026-01-03", StringComparison.Ordinal);
            var middle = markup.IndexOf("2026-01-02", StringComparison.Ordinal);
            var oldest = markup.IndexOf("2026-01-01", StringComparison.Ordinal);

            Assert.True(newest >= 0 && middle >= 0 && oldest >= 0, "all three rows should render");
            Assert.True(newest < middle && middle < oldest, "rows should be ordered newest-first");
        });
    }
}
