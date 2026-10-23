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
        var title = UiKit.Title("Sign in", 0);
        var subtitle = UiKit.Line("Unlock your encrypted vault", 1, dim: true);
        var label = UiKit.FieldLabel("Password", 4);
        _password = UiKit.Input(5, secret: true);
        _error = UiKit.Line(string.Empty, 9);

        var unlock = UiKit.Tile("Unlock", Pos.Center(), 11, Submit);
        var back = UiKit.Tile("Back to menu", Pos.Center(), 14, Shell.GotoMain);

        Add(title, subtitle, label, _password, _error, unlock, back);

        _password.Accepted += (_, _) => Submit();
    }

    private void Submit()
    {
        var status = Context.Login(_password.Text ?? string.Empty);

        switch (status)
        {
            case LoginStatus.Success:
                Shell.Replace(new StoreScreen(Context, Shell));
                break;
            case LoginStatus.NotInitialized:
                Shell.Replace(new InitStoreScreen(Context, Shell));
                break;
            case LoginStatus.IncorrectPassword:
                _error.Text = "Incorrect password";
                break;
        }
    }
}
