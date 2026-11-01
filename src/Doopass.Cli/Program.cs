using Doopass.Cli;
using Doopass.Cli.Api;
using Doopass.Tui;
using Microsoft.AspNetCore.Connections;

// Single entry point for Doopass.
//   doopass                         -> run the terminal UI
//   doopass serve [--port N] [--no-browser] [--lan] [--announce]
//                   [--tls <pfx>] [--tls-pass <password>]
//                                  -> run the web interface: Blazor UI, the REST API
//                                     for thin clients under /api, the /hubs/vault
//                                     SignalR channel and LAN discovery
//   doopass setup-tls [--pfx <path>] [--pass <pwd>] [--force]
//                                  -> create a self-signed certificate and store it
//                                     in settings.json for a later serve
//   doopass --help                 -> this list
return Run(args);

static int Run(string[] args)
{
    if (args.Length > 0 && args[0] is "--help" or "-h" or "help")
    {
        return ShowHelp();
    }

    if (args.Length > 0 && args[0] == "setup-tls")
    {
        return TlsSetup.Run(args[1..]);
    }

    var serve = args.Length > 0 && args[0] is "serve" or "--serve" or "--web";

    if (!serve)
    {
        return DoopassTui.Run();
    }

    var options = new WebHostOptions();

    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] is "--help" or "-h")
        {
            return ShowHelp();
        }

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
        else if (args[i] is "--tls" or "--tls-pfx" or "--tls-pass" or "--port" or "-p")
        {
            Console.Error.WriteLine($"{args[i]} needs a value (see doopass serve --help).");
            return 2;
        }
        else if (args[i].StartsWith('-'))
        {
            Console.Error.WriteLine($"Unknown option: {args[i]} (see doopass serve --help).");
            return 2;
        }
    }

    try
    {
        return WebHostFactory.RunWebHost(options);
    }
    catch (IOException ex) when (ex.InnerException is AddressInUseException)
    {
        Console.Error.WriteLine(
            $"Port {options.Port} is already in use. Stop the other Doopass instance " +
            $"(systemctl --user stop doopass, or the process shown by " +
            $"'ss -ltnp | grep :{options.Port}') or start with --port <other>.");
        return 1;
    }
}

static int ShowHelp()
{
    Console.WriteLine("""
        Doopass - encrypted password manager

        Usage:
          doopass                        run the terminal UI
          doopass serve [options]        run the web UI, REST API and discovery
          doopass setup-tls [options]    create a self-signed TLS certificate
          doopass --help                 show this help

        serve options:
          -p, --port N        HTTP port (default 7240)
          --no-browser        do not open the browser
          --lan, --network    listen on 0.0.0.0 and announce discovery
          --announce          force discovery announce
          --tls <pfx>         serve HTTPS from a PKCS#12 certificate
          --tls-pass <pwd>    certificate password
          -h, --help          show this help

        setup-tls options:
          --pfx <path>        certificate path (default ~/doopass/doopass.pfx)
          --pass <pwd>        PKCS#12 password (default random, stored in settings.json)
          --force             replace an existing certificate (phones must re-pair)
          -h, --help          show this help
        """);

    return 0;
}
