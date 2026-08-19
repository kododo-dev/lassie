using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Lassie.Tests.Infrastructure;

// Minimal IDialogService test double for EditLicenseTests. Only the ShowMessageBoxAsync
// overload EditLicense.razor actually calls is implemented as a normal method; every
// other member is an explicit interface implementation throwing NotSupportedException —
// out of scope for the tests that use this fake. The two events are never raised, which
// is fine: EditLicense talks to this fake directly and never touches MudDialogProvider.
public class FakeDialogService : IDialogService
{
    public TaskCompletionSource<bool?> MessageBoxResult { get; } = new();

    public Task<bool?> ShowMessageBoxAsync(
        string? title,
        string message,
        string yesText = "OK",
        string? noText = null,
        string? cancelText = null,
        DialogOptions? options = null) => MessageBoxResult.Task;

    event Func<IDialogReference, Task>? IDialogService.DialogInstanceAddedAsync
    {
        add { }
        remove { }
    }

    event Action<IDialogReference, DialogResult>? IDialogService.OnDialogCloseRequested
    {
        add { }
        remove { }
    }

    Task<bool?> IDialogService.ShowMessageBoxAsync(string? title, MarkupString message, string yesText, string? noText, string? cancelText, DialogOptions? options) =>
        throw new NotSupportedException();

    Task<bool?> IDialogService.ShowMessageBoxAsync(MessageBoxOptions options, DialogOptions? dialogOptions) =>
        throw new NotSupportedException();

    IDialogReference IDialogService.CreateReference() => throw new NotSupportedException();

    Task<IDialogReference> IDialogService.ShowAsync<TComponent>() => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync<TComponent>(string? title) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync<TComponent>(string? title, DialogOptions options) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync<TComponent>(DialogOptions options) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync<TComponent>(DialogParameters parameters) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync<TComponent>(string? title, DialogParameters parameters) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync<TComponent>(string? title, DialogParameters parameters, DialogOptions? options) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync<TComponent>(DialogParameters parameters, DialogOptions options) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync(Type component) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync(Type component, string? title) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync(Type component, string? title, DialogOptions options) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync(Type component, string? title, DialogParameters parameters) => throw new NotSupportedException();
    Task<IDialogReference> IDialogService.ShowAsync(Type component, string? title, DialogParameters parameters, DialogOptions options) => throw new NotSupportedException();

    void IDialogService.Close(IDialogReference dialog) => throw new NotSupportedException();
    void IDialogService.Close(IDialogReference dialog, DialogResult? result) => throw new NotSupportedException();
}
