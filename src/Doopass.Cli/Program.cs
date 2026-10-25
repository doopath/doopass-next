using Doopass.Cli.Api;
using Doopass.Tui;

// Single entry point for Doopass.
//   doopass                         -> run the terminal UI
//   doopass serve [--port N] [--no-browser] [--lan] [--announce]
//                   [--tls <pfx>] [--tls-pass <password>]
//                                  -> run the web interface: Blazor UI, the REST API
//                                     for thin clients under /api, the /hubs/vault
//                                     SignalR channel and LAN discovery
return Run(args);

static int Run(string[] args)
{
    var serve = args.Length > 0 && args[0] is "serve" or "--serve" or "--web";

    if (!serve)
    {
        return DoopassTui.Run();
    }

    var options = new WebHostOptions();

    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] is "--port" or "-p" && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed))
        {
            options = options with { Port = parsed };
            i++;
        }
        else if (args[i] == "--no-browser")
        {
            options = options with { OpenBrowser = false };
        }
        else if (args[i] is "--lan" or "--network")
        {
            options = options with { Lan = true };
        }
        else if (args[i] == "--announce")
        {
            options = options with { Announce = true };
        }
        else if (args[i] is "--tls" or "--tls-pfx" && i + 1 < args.Length)
        {
            options = options with { TlsPfx = args[i + 1] };
            i++;
        }
        else if (args[i] == "--tls-pass" && i + 1 < args.Length)
        {
            options = options with { TlsPassword = args[i + 1] };
            i++;
        }
    }

    return WebHostFactory.RunWebHost(options);
}
