using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The log-in screen (asks for the master password).</summary>
internal sealed class LoginScreen : ScreenBase
{
    private readonly TextField _password;
    private readonly Label _error;

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

        panel.Add(caption, _password, _error, unlock);

        var foot = UiKit.Hint("no vault yet? use Init store from the main menu", 15);
        foot.X = Pos.Center();
        foot.Width = Dim.Auto();

        Add(panel, foot);

        _password.Accepted += (_, _) => Submit();
    }

    public override string ScreenTitle => "sign in";

    public override bool KeepInHistory => false;

    public override string FooterHint => "h/j/k/l move   enter unlock   esc back   q quit";

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
