using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The log-in screen (asks for the master password).</summary>
internal sealed class LoginScreen : ScreenBase
{
    private readonly TextField _password;

    public LoginScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var title = new Label { Text = "Log in", X = Pos.Center(), Y = 3 };

        _password = new TextField
        {
            Secret = true,
            X = Pos.Center(),
            Y = 6,
            Width = 40,
        };

        var logIn = MakeButton("Log in", 9, Submit);
        var back = MakeButton("To Main Menu", 11, Shell.GotoMain);

        Add(title, _password, logIn, back);
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
                MessageBox.Query(App!, "Doopass", "Store is not initialized. Please sign up.", "OK");
                Shell.Replace(new InitStoreScreen(Context, Shell));
                break;
            case LoginStatus.IncorrectPassword:
                MessageBox.Query(App!, "Doopass", "Incorrect password!", "OK");
                break;
        }
    }

    private static Button MakeButton(string text, int y, Action action)
    {
        var button = new Button { Text = text, X = Pos.Center(), Y = y, Width = 24 };
        button.Accepted += (_, _) => action();
        return button;
    }
}
