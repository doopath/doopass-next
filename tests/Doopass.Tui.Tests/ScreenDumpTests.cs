using System.Text;
using Doopass.Core.Configuration;
using Doopass.Tui;
using Doopass.Tui.Screens;
using Terminal.Gui.App;
using Terminal.Gui.Time;
using Xunit;

namespace Doopass.Tui.Tests;

/// <summary>
/// Renders actual screens through the driver's output buffer and asserts on the
/// visible text. This is the only way to catch layout regressions (chrome rows,
/// panel borders, empty states) without a human at a terminal.
/// </summary>
public sealed class ScreenDumpTests : IDisposable
{
    private readonly string _home;
    private readonly IApplication _app;
    private readonly DoopassContext _context;
    private readonly DoopassWindow _window;

    public ScreenDumpTests()
    {
        _home = Path.Combine(Path.GetTempPath(), "doopass-tui-dump-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_home);
        Environment.SetEnvironmentVariable("DOOPASS_HOME", _home);

        _context = new DoopassContext(new Settings());

        _app = Application.Create(new VirtualTimeProvider());
        _app.Init();

        _window = new DoopassWindow(_context);
        _app.Driver!.SetScreenSize(100, 30);
        _app.Begin(_window);

        Render();
    }

    private void Render()
    {
        _app.LayoutAndDraw(true);
        _window.FocusCurrent();
        _app.LayoutAndDraw();
    }

    [Fact]
    public void Main_menu_renders_chrome_logo_and_menu()
    {
        var screen = Dump("main-menu");

        Assert.Contains("DOOPASS", screen);
        Assert.Contains("main menu", screen);

        // Header must be centred from the very first paint, not pushed to the left.
        var headerLine = screen.Split('\n').First(line => line.Contains("DOOPASS"));
        Assert.True(headerLine.TakeWhile(c => c == ' ').Count() > 20, $"header not centred: '{headerLine}'");
        Assert.Contains("Log in", screen);
        Assert.Contains("Settings", screen);
        Assert.Contains("enter select", screen);
    }

    [Fact]
    public void Store_renders_panels_empty_state_and_action_bar()
    {
        _window.Replace(new StoreScreen(_context, _window));
        Render();

        var screen = Dump("store");

        Assert.Contains("store", screen);
        Assert.Contains("search |", screen);
        Assert.Contains("secrets", screen);
        Assert.Contains("details", screen);
        Assert.Contains("no items yet", screen);
        Assert.DoesNotContain("[ Add pair ]", screen);
    }

    [Fact]
    public void Backups_renders_list_and_hotkey_footer()
    {
        _window.Replace(new BackupScreen(_context, _window));
        Render();

        var screen = Dump("backups");

        Assert.Contains("snapshots", screen);
        Assert.Contains("c create", screen);
        Assert.Contains("r restore", screen);
        Assert.DoesNotContain("Create backup", screen);
    }

    [Fact]
    public void Confirm_dialog_is_centred()
    {
        _window.Push(new ConfirmScreen(_context, _window, "Delete 'github'?", () => { }));
        Render();

        var lines = Dump("confirm").Split((char)10);
        var row = Array.FindIndex(lines, line => line.Contains("╭┤ confirm"));

        Assert.True(row > 5, $"dialog is not vertically centred (row {row})");
        Assert.True(lines[row].TakeWhile(c => c == ' ').Count() > 15, $"dialog is not horizontally centred: '{lines[row]}'");
    }

    [Fact]
    public void Edit_form_shows_the_value_and_centred_buttons()
    {
        _window.Push(new PairFormScreen(
            _context,
            _window,
            "Key",
            "Value",
            "github",
            "correct-horse-battery",
            "Update",
            (_, _) => { }));

        Render();

        var screen = Dump("pair-form");

        Assert.Contains("correct-horse-battery", screen);
        Assert.DoesNotContain("encrypted with the vault key", screen);
        Assert.Contains("[ Update ]", screen);
        Assert.Contains("[ Cancel ]", screen);
    }

    [Fact]
    public void Login_shows_layout_immediately()
    {
        _window.Replace(new LoginScreen(_context, _window));
        Render();

        var screen = Dump("login-layout");

        Assert.DoesNotContain("layout: —", screen);
        Assert.Contains("layout: ", screen);
    }

    [Fact]
    public void Help_renders_hotkey_table()
    {
        _window.Push(new HelpScreen(_context, _window));
        Render();

        var screen = Dump("help");

        Assert.Contains("keyboard", screen);
        Assert.Contains("move the selection", screen);
        Assert.Contains("esc", screen);
    }

    [Fact]
    public void Login_renders_form()
    {
        _window.Replace(new LoginScreen(_context, _window));
        Render();

        var screen = Dump("login");

        Assert.Contains("unlock vault", screen);
        Assert.Contains("Master password", screen);
        Assert.Contains("Unlock", screen);
        Assert.Contains("layout:", screen);
    }

    private string Dump(string name)
    {
        var buffer = _app.Driver!.GetOutputBuffer();
        var text = new StringBuilder();

        if (buffer?.Contents is not { } contents)
        {
            return string.Empty;
        }

        for (var row = 0; row < buffer.Rows; row++)
        {
            for (var col = 0; col < buffer.Cols; col++)
            {
                var cell = contents[row, col];
                text.Append(cell.Grapheme);
            }

            text.Append('\n');
        }

        var dir = "C:/Users/doopa/AppData/Local/hermes/cache/scratch/tui-dumps";
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".txt");
        File.WriteAllText(path, text.ToString());
        return text.ToString();
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
