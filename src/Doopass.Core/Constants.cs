namespace Doopass.Core;

/// <summary>Application-wide constants.</summary>
public static class Constants
{
    /// <summary>Application version.</summary>
    public const string Version = "5.0.0";

    /// <summary>Default PBKDF2 iteration count used when encrypting the store.</summary>
    public const int DefaultIterations = 100_000;

    /// <summary>Suffix appended to store backup files.</summary>
    public const string StoreBackupExtension = ".enc.bak";

    /// <summary>The ASCII logo rendered on the main menu.</summary>
    public const string Logo =
        """
        ______  _____  _____ ______   ___   _____  _____
        |  _  \|  _  ||  _  || ___ \ / _ \ /  ___|/  ___|
        | | | || | | || | | || |_/ // /_\ \\ `--. \ `--.
        | | | || | | || | | ||  __/ |  _  | `--. \ `--. \
        | |/ / \ \_/ /\ \_/ /| |    | | | |/\__/ //\__/ /
        |___/   \___/  \___/ \_|    \_| |_/\____/ \____/
        """;
}
