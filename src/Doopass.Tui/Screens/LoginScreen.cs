using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The log-in screen (asks for the master password).</summary>
internal sealed class LoginScreen : ScreenBase
{
    private readonly TextField _password;
    private readonly Label _error;
    private readonly Label _layout;

    public LoginScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var panel = UiKit.Panel("unlock vault", 0, 2, 60, 12);
        CenterView(panel, 60);

        var caption = UiKit.Hint("Master password", 0);
        caption.X = Pos.Center();
        caption.Width = Dim.Auto();
        _password = AddStop(UiKit.Input(1, secret: true, width: 52));
        _password.X = 2;

        _error = UiKit.Hint(string.Empty, 5);
        _error.X = 2;

        var unlock = AddStop(UiKit.Primary("Unlock", Pos.Center(), 7, Submit, width: 14));

        _layout = UiKit.Hint("layout: —", 9);
        _layout.Width = Dim.Auto();

        // Centre it inside the panel after layout: Pos.Center() on a fixed-width
        // FrameView is evaluated while the frame is still zero-wide and gets clipped.
        panel.SubViewLayout += (_, _) =>
            _layout.X = Math.Max(1, (panel.Viewport.Width - _layout.Text.Length) / 2);

        panel.Add(caption, _password, _error, unlock, _layout);

        // ValueChanged is not raised per keystroke on TextField; TextChanging is.
        _password.TextChanging += (_, e) => UpdateLayoutHint(e.Result ?? string.Empty);

        var foot = UiKit.Hint("no vault yet? use Init store from the main menu", 15);
        foot.X = Pos.Center();
        foot.Width = Dim.Auto();

        Add(panel, foot);

        _password.Accepted += (_, _) => Submit();
    }

    public override string ScreenTitle => "sign in";

    public override bool KeepInHistory => false;

    public override string FooterHint => "h/j/k/l move   enter unlock   esc back   q quit";

    /// <summary>
    /// Shows which keyboard layout produced the last typed character, so a user who
    /// typed the password in the wrong layout can see why it fails.
    /// </summary>
    private void UpdateLayoutHint(string text)
    {
        if (text.Length == 0)
        {
            _layout.Text = "layout: —";
            return;
        }

        var last = text[^1];

        _layout.Text = last switch
        {
            >= 'А' and <= 'я' => "layout: RU",
            'Ё' or 'ё' => "layout: RU",
            >= 'A' and <= 'z' => "layout: EN",
            _ => _layout.Text,
        };
    }

    private void Submit()
    {
        switch (Context.Login(_password.Text ?? string.Empty))
        {
            case LoginStatus.Success:
                Shell.Replace(new StoreScreen(Context, Shell));
                break;
            case LoginStatus.NotInitialized:
                Shell.Replace(new InitStoreScreen(Context, Shell));
                break;
            case LoginStatus.IncorrectPassword:
                _error.Text = "incorrect password";
                break;
        }
    }
}
