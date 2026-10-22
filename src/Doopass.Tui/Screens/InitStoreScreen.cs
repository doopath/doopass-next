using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The init-store (sign up) screen.</summary>
internal sealed class InitStoreScreen : ScreenBase
{
    private readonly TextField _password;

    public InitStoreScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var title = new Label { Text = "Init store", X = Pos.Center(), Y = 3 };

        _password = new TextField
        {
            Secret = true,
            X = Pos.Center(),
            Y = 6,
            Width = 40,
        };

        var signUp = MakeButton("Sign up", 9, Submit);
        var back = MakeButton("To Main Menu", 11, Shell.GotoMain);

        Add(title, _password, signUp, back);
    }

    private void Submit()
    {
        var status = Context.Init(_password.Text ?? string.Empty, out var message);

        switch (status)
        {
            case InitStatus.Success:
                MessageBox.Query(App!, "Doopass", message, "OK");
                Shell.Replace(new StoreScreen(Context, Shell));
                break;
            case InitStatus.AlreadyExists:
            case InitStatus.InvalidPassword:
                MessageBox.Query(App!, "Doopass", message, "OK");
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
