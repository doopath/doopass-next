using Doopass.Tui.Ui;
using Terminal.Gui.Views;

namespace Doopass.Tui.Screens;

/// <summary>The init-store (sign up) screen.</summary>
internal sealed class InitStoreScreen : ScreenBase
{
    private readonly TextField _password;
    private readonly TextField _confirm;
    private readonly Label _error;

    public InitStoreScreen(DoopassContext context, DoopassWindow shell) : base(context, shell)
    {
        var title = UiKit.Title("Create a vault", 1);
        var subtitle = UiKit.Line("Choose a strong master password", 2, dim: true);

        var passwordLabel = UiKit.FieldLabel("Password", 5);
        _password = UiKit.Input(6, secret: true);

        var confirmLabel = UiKit.FieldLabel("Repeat password", 11);
        _confirm = UiKit.Input(12, secret: true);

        _error = UiKit.Line(string.Empty, 17);

        var create = UiKit.MenuButton("Create vault", 19, Submit);
        var back = UiKit.MenuButton("Back", 21, () => Shell.Replace(new LoginScreen(Context, Shell)));

        Add(title, subtitle, passwordLabel, _password, confirmLabel, _confirm, _error, create, back);

        _confirm.Accepted += (_, _) => Submit();
    }

    private void Submit()
    {
        _error.Text = string.Empty;

        if (!string.Equals(_password.Text, _confirm.Text, StringComparison.Ordinal))
        {
            _error.Text = "Passwords do not match";
            return;
        }

        var status = Context.Init(_password.Text ?? string.Empty, out var message);

        switch (status)
        {
            case InitStatus.Success:
                Shell.Replace(new StoreScreen(Context, Shell));
                break;
            case InitStatus.AlreadyExists:
            case InitStatus.InvalidPassword:
                _error.Text = message;
                break;
        }
    }
}
