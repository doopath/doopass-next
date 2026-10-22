using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The settings screen: list sections with changeable values.</summary>
internal sealed class SettingsScreen : ScreenBase
{
    private readonly View _items;

    public SettingsScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var restore = MakeButton("Restore", 0, 0, 12, () =>
        {
            Context.Settings.RestoreDefaults();
            Shell.Replace(new SettingsScreen(Context, Shell));
        });

        var toMain = MakeButton("Main Menu", 14, 0, 13, Shell.GotoMain);
        var exit = MakeButton("Exit", 29, 0, 8, Shell.Quit);

        _items = new View
        {
            X = 0,
            Y = 2,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _items.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;

        Add(restore, toMain, exit, _items);

        BuildSections();
    }

    private void BuildSections()
    {
        var y = 0;

        foreach (var section in Context.Settings.Sections)
        {
            _items.Add(new Label
            {
                Text = $" {char.ToUpper(section[0])}{section[1..]}",
                X = 0,
                Y = y,
                Width = Dim.Fill(),
            });
            y += 1;

            foreach (var (key, value) in Context.Settings.GetSection(section))
            {
                var row = new SettingRow(section, key, value, EditSetting)
                {
                    X = 0,
                    Y = y,
                };

                _items.Add(row);
                y += 1;
            }

            y += 1;
        }
    }

    private void EditSetting(string section, string key, string value)
    {
        Shell.Push(new PairFormScreen(
            Context,
            Shell,
            "Key",
            "Value",
            key,
            value,
            "Update",
            (newKey, newValue) =>
            {
                Context.Settings.ModifyProperty(section, newKey, newValue);
                Shell.Replace(new SettingsScreen(Context, Shell));
            }));
    }

    private static Button MakeButton(string text, int x, int y, int width, Action action)
    {
        var button = new Button { Text = text, X = x, Y = y, Width = width };
        button.Accepted += (_, _) => action();
        return button;
    }

    private sealed class SettingRow : View
    {
        public SettingRow(string section, string key, string value, Action<string, string, string> onChange)
        {
            Width = Dim.Fill();
            Height = 1;

            var keyLabel = new Label { Text = $" {key}", X = 0, Y = 0, Width = 18 };
            var valueLabel = new Label { Text = value, X = 20, Y = 0, Width = Dim.Fill(14) };
            var change = new Button { Text = "Change", X = Pos.AnchorEnd(10), Y = 0, Width = 9 };

            change.Accepted += (_, _) => onChange(section, key, value);

            Add(keyLabel, valueLabel, change);
        }
    }
}
