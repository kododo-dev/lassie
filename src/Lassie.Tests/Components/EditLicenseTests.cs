using Bunit;
using Lassie.Components;
using Lassie.Components.Pages;
using Lassie.Data;
using Lassie.Data.Licenses;
using Lassie.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace Lassie.Tests.Components;

// IAsyncLifetime, not plain teardown: MudBlazor's AddMudServices() registers
// PointerEventsNoneService as IAsyncDisposable-only, which the DI container's
// synchronous Dispose() can't tear down. Disposing async first (via xUnit's
// IAsyncLifetime.DisposeAsync) lets the container clean up correctly before
// BunitContext's own base Dispose() runs as a now-idempotent no-op.
public class EditLicenseTests : BunitContext, IAsyncLifetime
{
    private readonly LassieDbContext dbContext;
    private readonly FakeDialogService dialogService = new();

    public EditLicenseTests()
    {
        Services.AddMudServices();
        Services.AddScoped<IDialogService>(_ => dialogService);
        Services.AddScoped<ThemeState>();
        JSInterop.Mode = JSRuntimeMode.Loose;

        dbContext = new LassieDbContext(new DbContextOptionsBuilder<LassieDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        Services.AddSingleton(dbContext);

        dbContext.Licenses.AddRange(
            new License { Id = 1, Label = "License A", ApiKeyHash = "hash-a", IsActive = true },
            new License { Id = 2, Label = "License B", ApiKeyHash = "hash-b", IsActive = true });
        dbContext.SaveChanges();
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await ((IAsyncDisposable)this).DisposeAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task DeactivatingLicenseA_DoesNotAffectLicenseB_WhenNavigationLandsMidConfirmation()
    {
        var cut = Render<EditLicense>(p => p.Add(x => x.Id, 1));

        await cut.InvokeAsync(() => cut.Find("input[type=checkbox]").Change(false));

        // The confirmation dialog is now awaiting dialogService.MessageBoxResult.
        // Simulate Blazor Server reusing this component instance for a navigation
        // to a different license's edit URL while that confirmation is still open.
        cut.Render(p => p.Add(x => x.Id, 2));

        // Relies on OnParametersSetAsync's query (EF Core InMemory) completing
        // synchronously above, so `license`/`Model` are already reassigned to
        // license B by the time this SetResult resumes the suspended
        // HandleIsActiveChanged continuation.
        dialogService.MessageBoxResult.SetResult(true);

        cut.WaitForAssertion(() =>
            Assert.Equal("Active", cut.Find("label.mud-switch").TextContent.Trim()));
    }

    [Fact]
    public async Task ConfirmingDeactivateDialog_TurnsTheSwitchToInactive()
    {
        var cut = Render<EditLicense>(p => p.Add(x => x.Id, 1));
        dialogService.MessageBoxResult.SetResult(true);

        await cut.InvokeAsync(() => cut.Find("input[type=checkbox]").Change(false));

        var switchLabel = cut.Find("label.mud-switch");
        Assert.Equal("Inactive", switchLabel.TextContent.Trim());
    }

    [Fact]
    public async Task CancelingDeactivateDialog_LeavesTheSwitchActive()
    {
        var cut = Render<EditLicense>(p => p.Add(x => x.Id, 1));
        dialogService.MessageBoxResult.SetResult(false);

        await cut.InvokeAsync(() => cut.Find("input[type=checkbox]").Change(false));

        var switchLabel = cut.Find("label.mud-switch");
        Assert.Equal("Active", switchLabel.TextContent.Trim());
    }
}
