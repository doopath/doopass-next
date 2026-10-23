using Doopass.Tui.Theme;
using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The settings screen: list sections with changeable values.</summary>
internal sealed class SettingsScreen : ScreenBase
{
    private readonly View _items;

    public SettingsScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var title = UiKit.Title("Settings", 0);
        var subtitle = UiKit.Line("application configuration", 1, dim: true);
        var restore = UiKit.InlineButton("Restore defaults", 1, 3, 18, () =>
            Shell.Confirm("Restore all settings to their defaults?", () =>
            {
                Context.Settings.RestoreDefaults();
                Shell.CloseAndReplace(new SettingsScreen(Context, Shell));
            }));

        _items = new View
        {
            X = 0,
            Y = 5,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
            TabStop = TabBehavior.NoStop,
            SchemeName = Monochrome.SchemeName,
        };
        _items.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;

        Add(title, subtitle, restore, _items);

        BuildSections();
    }

    private void BuildSections()
    {
        var y = 0;

        foreach (var section in Context.Settings.Sections)
        {
            _items.Add(new Label
            {
                Text = " " + SectionTitle(section),
                X = 0,
                Y = y,
                Width = Dim.Fill(),
                Height = 1,
                SchemeName = Monochrome.SchemeName,
            });
            y++;

            foreach (var pair in Context.Settings.GetSection(section))
            {
                var row = new SettingRow(section, pair.Key, pair.Value, BeginEdit)
                {
                    X = 0,
                    Y = y,
                };

                _items.Add(row);
                y++;
            }

            y++;
        }
    }

    private void BeginEdit(string section, string key, string value)
    {
        Shell.Push(new ValueFormScreen(
            Context,
            Shell,
            "Edit setting",
            $"{section}.{key}",
            value,
            newValue =>
            {
                Context.Settings.ModifyProperty(section, key, newValue);
                Shell.CloseAndReplace(new SettingsScreen(Context, Shell));
            }));
    }

    private static string SectionTitle(string section) =>
        section.Length == 0 ? section : char.ToUpper(section[0]) + section[1..];

    private sealed class SettingRow : View
    {
        public SettingRow(string section, string key, string value, Action<string, string, string> onChange)
        {
            Width = Dim.Fill();
            Height = 1;
            CanFocus = true;
            TabStop = TabBehavior.NoStop;
            SchemeName = Monochrome.SchemeName;

            var keyLabel = new Label
            {
                Text = " " + key,
                X = 0,
                Y = 0,
                Width = 20,
                Height = 1,
                SchemeName = Monochrome.SchemeName,
            };

            var valueLabel = new Label
            {
                Text = value,
                X = 21,
                Y = 0,
                Width = Dim.Fill(9),
                Height = 1,
                SchemeName = Monochrome.DimSchemeName,
            };

            Add(
                keyLabel,
                valueLabel,
                UiKit.InlineButton("change", Pos.AnchorEnd(8), 0, 8, () => onChange(section, key, value)));
        }
    }
}
