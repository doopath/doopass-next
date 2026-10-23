using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>Settings: every section is a group of "key = value" rows.</summary>
internal sealed class SettingsScreen : ScreenBase
{
    private readonly ListView _list;
    private readonly Label _status;

    /// <summary>Maps a visible list row to a setting; null for section headers.</summary>
    private readonly List<(string Section, string Key, string Value)?> _rows = new();

    public SettingsScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var restore = AddStop(UiKit.Primary("Restore defaults", 0, 0, () =>
            Shell.Confirm("Restore all settings to their defaults?", () =>
            {
                Context.Settings.RestoreDefaults();
                Shell.CloseAndReplace(new SettingsScreen(Context, Shell));
            }), width: 19));

        _status = UiKit.Hint(string.Empty, 3);

        var panel = UiKit.Panel("configuration", 0, 4, Dim.Fill(), Dim.Fill(4));
        _list = AddStop(UiKit.List([]));
        _list.Accepted += (_, _) => BeginEdit();
        panel.Add(_list);

        Add(restore, _status, panel);

        Build();
    }

    public override string ScreenTitle => "settings";

    public override string FooterHint => "h/j/k/l move   enter change value   esc back   q quit";

    private void Build()
    {
        _rows.Clear();
        var lines = new List<string>();

        foreach (var section in Context.Settings.Sections)
        {
            lines.Add("  " + SectionTitle(section).ToUpperInvariant());
            _rows.Add(null);

            foreach (var pair in Context.Settings.GetSection(section))
            {
                lines.Add($"    {pair.Key,-26}{pair.Value}");
                _rows.Add((section, pair.Key, pair.Value));
            }
        }

        _status.Text = $"  {_rows.Count(r => r is not null)} settings in {Context.Settings.Sections.Count()} sections";
        UiKit.SetItems(_list, lines);
    }

    private void BeginEdit()
    {
        if (_list.SelectedItem is not { } index || index < 0 || index >= _rows.Count)
        {
            return;
        }

        if (_rows[index] is not { } row)
        {
            _status.Text = "  section header - move to a setting to change it";
            return;
        }

        var (section, key, value) = row;

        Shell.Push(new ValueFormScreen(Context, Shell, "Edit setting", $"{section}.{key}", value, newValue =>
        {
            Context.Settings.ModifyProperty(section, key, newValue);
            Shell.CloseAndReplace(new SettingsScreen(Context, Shell));
        }));
    }

    private static string SectionTitle(string section) =>
        section.Length == 0 ? section : char.ToUpper(section[0]) + section[1..];
}
