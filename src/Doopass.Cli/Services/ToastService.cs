namespace Doopass.Cli.Services;

public enum ToastKind
{
    Success,
    Error,
    Info,
}

/// <summary>
/// In-memory toast queue (scoped to the current circuit). The host component renders
/// toasts from <see cref="Toasts"/> and subscribes to <see cref="Changed"/>.
/// </summary>
public sealed class ToastService
{
    private readonly object _gate = new();
    private readonly List<Toast> _toasts = new();

    public event Action? Changed;

    public IReadOnlyList<Toast> Toasts
    {
        get
        {
            lock (_gate)
            {
                return _toasts.ToArray();
            }
        }
    }

    public void Success(string title, string? message = null) => Show(ToastKind.Success, title, message);

    public void Error(string title, string? message = null) => Show(ToastKind.Error, title, message);

    public void Info(string title, string? message = null) => Show(ToastKind.Info, title, message);

    public void Dismiss(Guid id)
    {
        lock (_gate)
        {
            _toasts.RemoveAll(t => t.Id == id);
        }

        Changed?.Invoke();
    }

    private void Show(ToastKind kind, string title, string? message)
    {
        var toast = new Toast(Guid.NewGuid(), kind, title, message);

        lock (_gate)
        {
            _toasts.Add(toast);
        }

        Changed?.Invoke();

        _ = AutoDismissAsync(toast.Id);
    }

    private async Task AutoDismissAsync(Guid id)
    {
        await Task.Delay(TimeSpan.FromSeconds(3.8));
        Dismiss(id);
    }

    public sealed record Toast(Guid Id, ToastKind Kind, string Title, string? Message);
}
