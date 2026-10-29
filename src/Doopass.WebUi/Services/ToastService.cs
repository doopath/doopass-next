namespace Doopass.WebUi.Services;

/// <summary>Visual category of a toast message.</summary>
public enum ToastKind
{
    /// <summary>The operation succeeded.</summary>
    Success,

    /// <summary>The operation failed.</summary>
    Error,

    /// <summary>Neutral information or a hint.</summary>
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

    /// <summary>Raised after any toast is added or removed; UI components re-render on it.</summary>
    public event Action? Changed;

    /// <summary>Snapshot of the toasts currently on screen.</summary>
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

    /// <summary>Shows a success toast.</summary>
    /// <param name="title">Short headline.</param>
    /// <param name="message">Optional detail line.</param>
    public void Success(string title, string? message = null) => Show(ToastKind.Success, title, message);

    /// <summary>Shows an error toast.</summary>
    /// <param name="title">Short headline.</param>
    /// <param name="message">Optional detail line.</param>
    public void Error(string title, string? message = null) => Show(ToastKind.Error, title, message);

    /// <summary>Shows an informational toast.</summary>
    /// <param name="title">Short headline.</param>
    /// <param name="message">Optional detail line.</param>
    public void Info(string title, string? message = null) => Show(ToastKind.Info, title, message);

    /// <summary>Removes a toast immediately (also called by the auto-dismiss timer).</summary>
    /// <param name="id">Identifier of the toast to remove.</param>
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

    /// <summary>One toast message.</summary>
    /// <param name="Id">Unique identifier, used for dismissal.</param>
    /// <param name="Kind">Visual category.</param>
    /// <param name="Title">Short headline.</param>
    /// <param name="Message">Optional detail line.</param>
    public sealed record Toast(Guid Id, ToastKind Kind, string Title, string? Message);
}
