namespace SubnauticaLauncher.Installer;

public sealed class DepotInstallAuthOptions
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public bool RememberPassword { get; init; }
    public bool UseRememberedLoginOnly { get; init; }
    public bool PreferTwoFactorCode { get; init; } = true;
}
