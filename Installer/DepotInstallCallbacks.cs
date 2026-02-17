using System;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Installer;

public sealed class DepotInstallCallbacks
{
    public Action<string>? OnStatus { get; init; }
    public Action<string>? OnOutput { get; init; }
    public Action<double>? OnProgress { get; init; }
    public Func<DepotInstallPromptRequest, Task<string?>>? RequestInputAsync { get; init; }
}

public sealed class DepotInstallPromptRequest
{
    public string Prompt { get; init; } = "";
    public bool IsSecret { get; init; }
}
