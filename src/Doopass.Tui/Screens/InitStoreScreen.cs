using Doopass.Tui.Ui;
using Terminal.Gui.ViewBase;
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
        var panel = UiKit.Panel("create vault", 0, 1, 60, 19);
        CenterView(panel, 60);

        var caption = UiKit.Hint("Choose a strong master password", 0);
        caption.X = Pos.Center();
        caption.Width = Dim.Auto();

        var passwordLabel = UiKit.Hint("Password", 1);
        _password = AddStop(UiKit.Input(2, secret: true, width: 52));
        _password.X = 2;

        var confirmLabel = UiKit.Hint("Repeat password", 6);
        _confirm = AddStop(UiKit.Input(7, secret: true, width: 52));
        _confirm.X = 2;

        _error = UiKit.Hint(string.Empty, 11);
        _error.X = 2;

        var create = AddStop(UiKit.Primary("Create vault", Pos.Center(), 13, Submit, width: 18));

        panel.Add(caption, passwordLabel, _password, confirmLabel, _confirm, _error, create);

        var foot = UiKit.Hint("stored under ~/doopass/appdata/store.enc", 21);
        foot.X = Pos.Center();
        foot.Width = Dim.Auto();

        Add(panel, foot);

        _confirm.Accepted += (_, _) => Submit();
    }

    public override string ScreenTitle => "init store";

    public override bool KeepInHistory => false;

    public override string FooterHint => "h/j/k/l move   enter create   esc back   q quit";

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
