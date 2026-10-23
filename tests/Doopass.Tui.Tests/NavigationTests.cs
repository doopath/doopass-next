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
        _window.Replace(new BackupScreen(_context, _window));
        Settle();

        var buttons = FindAll<Label>(_window).Where(label => label.CanFocus).ToList();
        Assert.True(buttons.Count >= 3, $"expected action items ({Dump()})");

        var first = buttons[0];
        first.SetFocus();
        Assert.True(first.HasFocus, $"first button did not take focus ({Dump()})");

        Press(Key.Tab);
        Assert.False(first.HasFocus, $"Tab did not move focus ({Dump()})");

        Press(Key.Tab.WithShift);
        Assert.True(first.HasFocus, $"Shift+Tab did not move focus back ({Dump()})");
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
    public void Details_pane_does_not_trap_focus()
    {
        var storePath = _context.Settings.StoreFile;
        Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);

        var store = new Store("Passw0rd!x", storePath);
        store.AddProperty("github", "correct horse battery staple");

        Assert.Equal(LoginStatus.Success, _context.Login("Passw0rd!x"));

        _window.Replace(new StoreScreen(_context, _window));
        Settle();

        var details = FindAll<TextView>(_window).Single();
        details.SetFocus();
        Assert.True(details.HasFocus, $"details pane did not take focus ({Dump()})");

        Press(Key.L);
        Assert.False(details.HasFocus, $"L did not leave the details pane ({Dump()})");

        details.SetFocus();
        Press(Key.K);
        Assert.False(details.HasFocus, $"K at the top did not leave the details pane ({Dump()})");
    }

    [Fact]
    public void Typing_still_reaches_a_text_field()
    {
        _window.Replace(new LoginScreen(_context, _window));
        Settle();

        var field = FindAll<TextField>(_window).Single();
        field.SetFocus();
        Assert.True(field.HasFocus, $"field did not take focus ({Dump()})");

        _app.InjectKey(Key.J);
        Assert.Equal("j", field.Text);
    }

    /// <summary>Feeds a key to the global entry point wired in <c>DoopassTui</c>.</summary>
    private void Press(Key key)
    {
        _window.RouteKey(new Key(key.KeyCode), _app);
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
