using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>Backup management: create, restore, delete.</summary>
internal sealed class BackupScreen : ScreenBase
{
    private readonly ListView _list;
    private readonly Label _status;
    private List<string> _names = new();

    public BackupScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        _status = UiKit.Hint(string.Empty, 0);

        var panel = UiKit.Panel("snapshots", 0, 2, Dim.Fill(), Dim.Fill(2));
        _list = AddStop(UiKit.List([]));
        _list.Accepted += (_, _) => ConfirmRestore(SelectedName);
        _list.ValueChanged += (_, _) => UpdateStatus();
        panel.Add(_list);

        Add(_status, panel);

        Refresh();
    }

    public override string ScreenTitle => "backups";

    public override string FooterHint =>
        "h/j/k/l move   c create   r restore   d delete   esc back   q quit";

    /// <summary>Hotkeys routed from <see cref="DoopassWindow"/>: c create, r restore, d delete.</summary>
    public void RunShortcut(char shortcut)
    {
        switch (shortcut)
        {
            case 'c':
                CreateBackup();
                break;
            case 'r':
                ConfirmRestore(SelectedName);
                break;
            case 'd':
                ConfirmDelete(SelectedName);
                break;
        }
    }

    private void CreateBackup()
    {
        Context.Backups.MakeBackup();
        Refresh();
    }

    private string? SelectedName =>
        _list.SelectedItem is { } index && index >= 0 && index < _names.Count ? _names[Math.Clamp(index, 0, _names.Count - 1)] : null;

    private void Refresh()
    {
        _names = Context.Backups.GetBackupNames().ToList();

        UiKit.SetItems(_list, _names.Count == 0
            ? [UiKit.Empty("no backups yet")]
            : _names.Select(name => "  " + name).ToList());

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (_names.Count == 0)
        {
            _status.Text = "  press c to create the first snapshot";
            return;
        }

        _status.Text = $"  {_names.Count} snapshot{(_names.Count == 1 ? string.Empty : "s")}" +
                       (SelectedName is { } name ? $"   selected: {name}" : string.Empty);
    }

    private void ConfirmRestore(string? name)
    {
        if (name is null)
        {
            return;
        }

        Shell.Confirm($"Restore vault from '{name}'?", () =>
        {
            Context.Backups.SwapStoreWithBackup(name + Context.Settings.BackupExtension);
            Context.ReloadStore();
            Shell.CloseAndReplace(new BackupScreen(Context, Shell));
        });
    }

    private void ConfirmDelete(string? name)
    {
        if (name is null)
        {
            return;
        }

        Shell.Confirm($"Delete backup '{name}'?", () =>
        {
            Context.Backups.DeleteBackup(name + Context.Settings.BackupExtension);
            Shell.CloseAndReplace(new BackupScreen(Context, Shell));
        });
    }
}
