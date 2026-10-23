using Doopass.Core.Configuration;
using Doopass.Core.Storage;
using Doopass.Tui;
using Doopass.Tui.Screens;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Testing;
using Terminal.Gui.Time;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Xunit;

namespace Doopass.Tui.Tests;

/// <summary>
/// Navigation behaviour against a real Terminal.Gui application and a real window.
/// Keys are fed to <see cref="DoopassWindow.RouteKey"/>, the single entry point that
/// <c>DoopassTui</c> wires to <c>IApplication.Keyboard.KeyDown</c>. That is one call
/// away from the real input path and, unlike direct view injection, does not
/// double-dispatch a key into the focused view.
/// </summary>
public sealed class NavigationTests : IDisposable
{
    private readonly string _home;
    private readonly IApplication _app;
    private readonly DoopassContext _context;
    private readonly DoopassWindow _window;

    public NavigationTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "doopass-tui-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        Environment.SetEnvironmentVariable("DOOPASS_HOME", _home);

        _context = new DoopassContext(new Settings());

        var time = new VirtualTimeProvider();
        _app = Application.Create(time);
        _app.Init();

        _window = new DoopassWindow(_context);

        // Same wiring as DoopassTui.Run: the router sees every key first.
        _app.Keyboard.KeyDown += (_, key) => _window.RouteKey(key, _app);

        _app.Driver!.SetScreenSize(100, 30);
        _app.Begin(_window);
        Settle();
    }

    [Fact]
    public void Main_menu_moves_with_j_k_and_arrows()
    {
        var rows = MenuRows();
        Assert.True(rows[0].HasFocus, $"first row should start focused ({Dump()})");

        Press(Key.CursorDown);
        Assert.True(rows[1].HasFocus, "down arrow did not move");

        Press(Key.J);
        Assert.True(rows[2].HasFocus, "j did not move down");

        Press(Key.CursorUp);
        Assert.True(rows[1].HasFocus, "up arrow did not move");

        Press(Key.K);
        Assert.True(rows[0].HasFocus, "k did not move up");
    }

    private List<Label> MenuRows() =>
        FindAll<Label>(_window).Where(label => label.CanFocus).ToList();

    [Fact]
    public void Tab_moves_focus_forward_and_shift_tab_back()
    {
        _window.Replace(new LoginScreen(_context, _window));
        Settle();

        var field = FindAll<TextField>(_window).Single();
        field.SetFocus();
        Assert.True(field.HasFocus, $"password field did not take focus ({Dump()})");

        Press(Key.Tab);
        Assert.False(field.HasFocus, $"Tab did not move focus ({Dump()})");

        Press(Key.Tab.WithShift);
        Assert.True(field.HasFocus, $"Shift+Tab did not move focus back ({Dump()})");
    }

    [Fact]
    public void Esc_goes_back_one_screen()
    {
        _window.Push(new BackupScreen(_context, _window));
        Settle();

        Press(Key.Esc);
        Settle();

        // Back on the main menu: its first row takes focus again.
        Assert.True(MenuRows()[0].HasFocus, $"Esc did not go back ({Dump()})");
    }

    [Fact]
    public void Store_list_scrolls_with_j_arrows_and_page_keys()
    {
        var storePath = _context.Settings.StoreFile;
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);

        var store = new Store("Passw0rd!x", storePath);
        for (var i = 0; i < 40; i++)
        {
            store.AddProperty($"key-{i:00}", $"value-{i}");
        }

        Assert.Equal(LoginStatus.Success, _context.Login("Passw0rd!x"));

        _window.Replace(new StoreScreen(_context, _window));
        Settle();

        var list = FindAll<ListView>(_window).Single();
        list.SetFocus();
        Assert.True(list.HasFocus, $"store list did not take focus ({Dump()})");

        for (var i = 0; i < 20; i++)
        {
            Press(Key.J);
        }

        Assert.Equal(20, list.SelectedItem);

        Press(Key.CursorDown);
        Assert.Equal(21, list.SelectedItem);

        Press(Key.End);
        Assert.Equal(39, list.SelectedItem);

        Press(Key.Home);
        Assert.Equal(0, list.SelectedItem);

        var viewport = list.Viewport.Height;
        Assert.True(viewport > 5, $"list viewport height is {viewport} ({Dump()})");

        Press(Key.PageDown);
        Assert.True(list.SelectedItem > 0, $"PageDown did not move the selection ({Dump()})");

        Press(Key.PageUp);
        Assert.Equal(0, list.SelectedItem);
    }

    [Fact]
    public void Esc_returns_to_main_menu_through_replace_history()
    {
        _window.Replace(new LoginScreen(_context, _window));
        Settle();

        // Sign-in is transitional: it must not become the back target.
        _window.Replace(new StoreScreen(_context, _window));
        Settle();

        Press(Key.Esc);
        Settle();

        var rows = MenuRows();
        Assert.True(rows.Count > 0, "expected the main menu after Esc");
        Assert.True(rows[0].HasFocus, $"Esc should go back to the main menu, not leave the app ({Dump()})");
    }

    [Fact]
    public void Esc_from_the_store_keeps_the_vault_unlocked()
    {
        OpenStore();

        Press(Key.Esc);
        Settle();

        Assert.NotNull(_context.CurrentStore);
        Assert.Contains("Open store", MenuRows()[0].Text?.ToString());
    }

    [Fact]
    public void Reopening_the_store_does_not_ask_for_the_password_again()
    {
        OpenStore();

        Press(Key.Esc);
        Settle();

        // First menu row is "Open store" when a session is already unlocked.
        // Enter is not a global key, so it goes to the focused row itself.
        MenuRows()[0].InvokeCommand(Command.Accept);
        Settle();

        Assert.NotNull(_context.CurrentStore);
        Assert.True(FindAll<TextView>(_window).Count == 1, $"Enter should reopen the store ({Dump()})");
    }

    [Fact]
    public void G_locks_the_vault_and_returns_to_the_menu()
    {
        OpenStore();

        // The hotkey belongs to the list, not to the search field (where it is text).
        FindAll<ListView>(_window).Single().SetFocus();
        Press(Key.G);
        Settle();

        Assert.Null(_context.CurrentStore);
        Assert.Contains("Log in", MenuRows()[0].Text?.ToString());
    }

    /// <summary>Creates a store on disk and lands on the store screen, logged in.</summary>
    private void OpenStore()
    {
        var storePath = _context.Settings.StoreFile;
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);

        if (!File.Exists(storePath))
        {
            var store = new Store("Passw0rd!x", storePath);
            store.AddProperty("github", "correct horse battery staple");
        }

        Assert.Equal(LoginStatus.Success, _context.Login("Passw0rd!x"));

        _window.Replace(new LoginScreen(_context, _window));
        Settle();
        _window.Replace(new StoreScreen(_context, _window));
        Settle();
    }

    [Fact]
    public void Zero_opens_restore_defaults_from_the_settings_screen()
    {
        _window.Replace(new SettingsScreen(_context, _window));
        Settle();

        Press(Key.D0);
        Settle();

        var texts = FindAll<Label>(_window)
            .Select(label => label.Text?.ToString() ?? string.Empty)
            .ToList();

        Assert.Contains(texts, text => text.Contains("Restore all settings"));
    }

    [Fact]
    public void Tab_cycles_only_search_and_list_on_the_store_screen()
    {
        var storePath = _context.Settings.StoreFile;
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);

        var store = new Store("Passw0rd!x", storePath);
        store.AddProperty("github", "correct horse battery staple");

        Assert.Equal(LoginStatus.Success, _context.Login("Passw0rd!x"));

        _window.Replace(new StoreScreen(_context, _window));
        Settle();

        var search = FindAll<TextField>(_window).Single();
        var list = FindAll<ListView>(_window).Single();
        Assert.True(search.HasFocus, $"search should start focused ({Dump()})");

        Press(Key.Tab);
        Assert.True(list.HasFocus, $"Tab should move search -> list ({Dump()})");

        Press(Key.Tab);
        Assert.True(search.HasFocus, $"Tab should wrap list -> search, not details ({Dump()})");

        var details = FindAll<TextView>(_window).Single();
        Assert.False(details.CanFocus, "the details pane must not be focusable");
    }

    [Fact]
    public void Typing_still_reaches_a_text_field()
    {
        _window.Replace(new LoginScreen(_context, _window));
        Settle();

        var field = FindAll<TextField>(_window).Single();
        field.SetFocus();
        Assert.True(field.HasFocus, $"field did not take focus ({Dump()})");

        // Regression: H/J/K/L and Q are global hotkeys, but an editable field must
        // receive them as text.
        foreach (var key in new[] { Key.H, Key.J, Key.K, Key.L, Key.Q })
        {
            _app.InjectKey(key);
        }

        Assert.Equal("hjklq", field.Text);
    }

    /// <summary>Feeds a key to the global entry point wired in <c>DoopassTui</c>.</summary>
    private void Press(Key key)
    {
        _window.RouteKey(key, _app);
        _app.LayoutAndDraw();
    }

    private void Settle()
    {
        _app.LayoutAndDraw(true);
        _window.FocusCurrent();
        _app.LayoutAndDraw();
    }

    private string Dump()
    {
        var focused = _app.Navigation?.GetFocused();

        return $"window={_window.Frame} focused={focused?.GetType().Name}:{focused?.Frame} " +
               string.Join(" ", _window.SubViews.Select(v => $"{v.GetType().Name}={v.Frame}"));
    }

    private static List<T> FindAll<T>(View root) where T : View
    {
        var found = new List<T>();

        foreach (var sub in root.SubViews)
        {
            if (sub is T typed)
            {
                found.Add(typed);
            }

            found.AddRange(FindAll<T>(sub));
        }

        return found;
    }

    public void Dispose()
    {
        _app.Dispose();
        Environment.SetEnvironmentVariable("DOOPASS_HOME", null);

        try
        {
            Directory.Delete(_home, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
