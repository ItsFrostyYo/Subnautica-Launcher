# Subnautica Launcher

This is the clean rebuild foundation for the Subnautica Launcher.

## Updates

Launcher updates use [SNLUpdates.xml](Updates/SNLUpdates.xml), and updater updates use [SNLUUpdates.xml](Updates/SNLUUpdates.xml). Both use LiveSplit-style XML manifests and repository-hosted payloads instead of requiring GitHub release assets. Launcher checks run before plugin checks and update work is deferred while Subnautica is running.

The launcher is a WPF application with a native Dear ImGui host for its secondary windows. Runtime files live under `Dependencies`, while user configuration and plugin settings remain under `Data`.

## Plugins

LiveSplit plugins run through an isolated host mode of `SubnauticaLauncher.exe`. LiveSplit's managed runtime is bundled into the launcher's single-file build, so a separate LiveSplit installation or plugin-host executable is not required. The host mode creates the normal LiveSplit component state and message loop, calls the plugin through `IComponentFactory` and `IComponent`, and keeps each plugin's runtime behavior inside the plugin itself. The launcher owns installation, enable/disable state, updates, and ImGui settings pages that save to the plugin's standard XML settings format.

Installed plugin binaries live under `Plugins`. Each plugin keeps one launcher-owned `plugin.json` file for installed/enabled state and one native `settings.xml` file for its component settings under `Data/Plugins`. Runtime logs, including `SubnauticaMarco.log`, live under `Logs`. Rebuilds and launcher updates preserve all three user-owned directories.

Installed plugins are checked against their own LiveSplit update manifests. Updates replace the component DLL before an enabled plugin is loaded, while preserving its enabled state and settings.

## Development

Build the solution:

```powershell
.\Scripts\Build.ps1
```

Create a release package:

```powershell
.\Scripts\Build-ReleasePackage.ps1 -Clean
```

The packaging script replaces the top-level `Release` folder. It contains `SubnauticaLauncher.zip`, loose launcher/updater executables, both update manifests, and every dependency referenced by the launcher update feed.
