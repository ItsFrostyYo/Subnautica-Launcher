using SubnauticaLauncher.Updates;
using System.Collections.Generic;
using System.Linq;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Updates;

public class UpdateInfoViewModel
{
    public UpdateEntry LatestUpdate { get; }
    public IReadOnlyList<UpdateEntry> PreviousUpdates { get; }

    public UpdateInfoViewModel()
    {
        var history = Updates.History;

        LatestUpdate = history.Length > 0
            ? history[0]
            : new UpdateEntry();

        PreviousUpdates = history.Length > 1
            ? history.Skip(1).ToList()
            : new List<UpdateEntry>();
    }
}