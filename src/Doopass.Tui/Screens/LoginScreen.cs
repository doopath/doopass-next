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
    private const int ActionWidth = 14;

    private View? _unlock;

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

        var unlock = AddStop(UiKit.Primary("Unlock", Pos.Center(), 7, Submit, width: ActionWidth));
        _unlock = unlock;

        _layout = UiKit.Hint(string.Empty, 9);

        // Same width and X as the Unlock action, with the text padded to the centre:
        // that keeps the two captions on one vertical axis (Label does not centre text).
        _layout.Width = ActionWidth;
        panel.SubViewLayout += (_, _) => _layout.X = unlock.Frame.X;

        panel.Add(caption, _password, _error, unlock, _layout);

        // ValueChanged is not raised per keystroke on TextField; TextChanging is.
        _password.TextChanging += (_, e) => UpdateLayoutHint(e.Result ?? string.Empty);

        UpdateLayoutHint(string.Empty);

        var foot = UiKit.Hint("no vault yet? use Init store from the main menu", 15);
        foot.X = Pos.Center();
        foot.Width = Dim.Auto();

        Add(panel, foot);

        _password.Accepted += (_, _) => Submit();
    }

    public override string ScreenTitle => "sign in";

    public override void OnShown()
    {
        // Re-read the system layout every time the screen opens.
        if (string.IsNullOrEmpty(_password.Text))
        {
            UpdateLayoutHint(string.Empty);
        }
    }

    public override bool KeepInHistory => false;

    public override string FooterHint => "h/j/k/l move   enter unlock   esc back   q quit";

    /// <summary>
    /// Shows which keyboard layout produced the last typed character, so a user who
    /// typed the password in the wrong layout can see why it fails.
    /// </summary>
    private void UpdateLayoutHint(string text)
    {
        _layout.Text = "layout: " + KeyboardLayout.ForText(text);

        if (_unlock is not null)
        {
            _layout.X = _unlock.Frame.X;
        }

        _layout.SetNeedsDraw();
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
