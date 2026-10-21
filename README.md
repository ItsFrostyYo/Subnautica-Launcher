# Subnautica Launcher

Subnautica Launcher is a Windows launcher for managing and playing multiple versions of the original **Subnautica** from one place.

It is a complete rebuild of the discontinued Subnautica Trilogy Launcher, with a cleaner version-management workflow, native secondary windows, saved Subnautica settings profiles, and a plugin system for optional tools.

Repository:

- https://github.com/ItsFrostyYo/Subnautica-Launcher

## Supported Games

The current launcher supports:

- **Subnautica**

It does not currently support **Subnautica: Below Zero** or **Subnautica 2**. Below Zero support is planned for the future. Subnautica 2 support from the discontinued launcher is not included in this rebuild.

## What It Does

The launcher can:

- install supported Subnautica versions, from the first Early Access builds through current releases
- add an existing Subnautica installation and detect its known build automatically
- organize, rename, reorder, and launch managed versions
- save launch options separately for each version
- launch versions directly with the correct Steam AppID handling
- optionally use Steam-compatible folder switching
- detect an existing Subnautica Speedrunning Mod installation and use its launch command
- save Subnautica in-game settings and copy them between managed versions
- apply saved in-game settings automatically to new installs
- install and manage optional launcher plugins
- update the launcher, updater, and installed plugins

## Requirements

- 64-bit Windows 10 or Windows 11
- Steam installed
- a Steam account that owns Subnautica
- internet access for version downloads, launcher updates, and plugin installation
- enough free disk space for every Subnautica version you want to keep

Release builds are self-contained, so a separate .NET installation is not required.

## First-Time Setup

1. Download the latest `SubnauticaLauncher.zip` from [GitHub Releases](https://github.com/ItsFrostyYo/Subnautica-Launcher/releases).
2. Extract the entire ZIP to a normal folder.
3. Keep `SubnauticaLauncher.exe`, `SNLUpdater.exe`, and the `Dependencies` folder together.
4. Run `SubnauticaLauncher.exe`.

Do not run the launcher from inside the ZIP. The launcher needs write access to its folder so it can save configuration, install plugins, write logs, and update itself.

## Main Window

The main navigation contains:

- `Play`
- `Settings`
- `Plugins`
- `Launcher Info`

The left sidebar shows the selected version, active or running version status, and the main `Launch` and `Switch` controls.

## Play Tab

The Play tab displays every managed Subnautica version as a tile. From here you can:

- select and launch a version
- switch to or restart a version
- open its game folder
- edit its launcher settings
- add another version
- change the order of version tiles

Tile colors show whether a version is idle, active, or currently launched.

### Installing a Version

To download a supported Subnautica build:

1. Open `Play`.
2. Press `Add Version`.
3. Choose `Install New Version`.
4. Select the version you want.
5. Enter your Steam username and password, then choose a detected Steam library.
6. Complete Steam Guard, email-code, authenticator, or mobile-approval prompts if Steam requests them.
7. Wait for the download and installation to finish.

The launcher uses DepotDownloader for historical Steam manifests. It downloads DepotDownloader automatically when required and shows install status, progress, estimated time, authentication prompts, and output in the install window.

The login window also supports DepotDownloader's remembered-login flow. Remembering a login is optional and is not enabled automatically.

Cancelling a download asks for confirmation and attempts to remove the incomplete installation folder.

### Adding an Existing Version

To manage a Subnautica version that is already installed:

1. Open `Play`.
2. Press `Add Version`.
3. Choose `Add Existing Version`.
4. Browse to the folder containing `Subnautica.exe`.
5. Let the launcher identify the build from its game files.
6. Confirm the display name and managed folder name.
7. Press `Add Version`.

The launcher only accepts recognized Subnautica builds and prevents the same folder from being added twice. The folder name `Subnautica` is reserved for Steam-compatible folder switching and cannot be used as a normal managed folder name.

If the selected installation contains the supported Subnautica Speedrunning Mod layout, the launcher detects it automatically.

### Launching a Version

Select a version and press `Launch`.

By default, the launcher:

- starts `Subnautica.exe` directly from the selected folder
- maintains `steam_appid.txt` with Subnautica's Steam AppID (`264710`)
- applies that version's saved launch options
- uses `Launch Mod.cmd` instead when an existing Subnautica Speedrunning Mod installation is detected

If another managed Subnautica version is already running, `Launch` closes it before starting the selected version. When the selected version is already running, the button changes to `Close`.

### Switching and Restarting

The `Switch` button adapts to the current state:

- when no game is running, it marks the selected version as the active version
- when another version is running, it closes that version and launches the selected one
- when the selected version is running, it changes to `Restart`

### Changing Version Order

Press `Change Ordering` to show left and right controls under each version. Move the tiles into the order you want, then press `Done Ordering`.

The order is saved in the launcher configuration.

### Editing a Version

Press the gear button under a version tile to open its settings. You can:

- change its display name
- change its managed folder name
- choose a different tile icon
- set per-version launch options
- detect Subnautica launch options from Steam
- remove the entry from the launcher without deleting the game
- delete both the launcher entry and its game files

Examples of launch options include:

- `-novr`
- `-high`

Launch options apply to direct launches. They are not appended when `Force to Launch With Steam` is enabled because Steam owns that launch flow.

## Subnautica Settings Profiles

Open `Settings` and press `Manage` under `Save Subnautica Settings` to manage an in-game settings profile.

You can:

- save `options.bin` from a managed version as the launcher profile
- inspect the readable settings found in the saved profile
- replace the saved profile with settings from another version
- copy the saved profile to another managed version
- choose whether new version installs receive the saved profile automatically

A version must have been launched far enough to create its Subnautica options file before its settings can be saved.

## Plugins Tab

The rebuild replaces the discontinued launcher's fixed Tools page with an optional plugin system.

The Plugins tab provides:

- `Installed` and `Browse Plugins` views
- plugin search
- one-click installation
- enable and disable controls
- plugin-specific settings
- update detection and installation
- plugin removal

Installed plugin binaries are stored under `Plugins`. Launcher-owned plugin state and native plugin settings are stored under `Data/Plugins`.

### Currently Available Plugin

The current catalog includes the **Subnautica Reset Macro** plugin, displayed as `Subnautica Marco` in the launcher. It supports configurable automated resets for relevant 2018, 2021, 2023, and 2025 Subnautica versions and the Subnautica Speedrunning Mod.

Its settings include:

- enable or disable the reset macro
- configurable reset hotkey
- Survival, Hardcore, Creative, Freedom, and BetterRNG game modes
- optional explosion-time resetting
- built-in explosion-time presets
- a custom minimum and maximum explosion-time range

### Plugin Runtime

Compatible LiveSplit component plugins run through an isolated host mode built into `SubnauticaLauncher.exe`. A separate LiveSplit installation is not required.

The launcher manages installation, enabled state, updates, process startup, and settings. If an enabled plugin process stops unexpectedly, the launcher can restart its host. Plugin updates preserve the plugin's enabled state and settings.

Launcher and plugin update work is deferred while Subnautica is running.

## Settings Tab

### Background

Choose one of the included Subnautica or Below Zero background images, or select `Custom` and browse to your own image.

The selected background is also used by the launcher's native secondary windows.

### Force to Launch With Steam

This option is disabled by default.

When disabled, managed versions launch directly from their own folders and keep `steam_appid.txt` for Steam integration.

When enabled, the selected version must be inside a detected Steam `steamapps/common` library. The launcher temporarily moves it to Steam's active `Subnautica` folder and restores the previously active managed version to its configured folder name.

The launcher refuses the switch if Steam's active `Subnautica` folder is not managed by the launcher or if a destination folder already exists. This prevents an unmanaged installation from being overwritten.

## Launcher Info

The Launcher Info page shows the launcher update history and provides links to:

- GitHub releases
- the developer's YouTube channel
- the Subnautica speedrunning Discord

## Updates

The launcher checks for launcher updates before checking installed plugins.

When a launcher update is available, you can choose `Later` or `Update Now`. The visible update flow:

1. updates `SNLUpdater.exe` if required
2. downloads and validates the launcher payload
3. closes the launcher
4. replaces changed files through SNLUpdater
5. reopens the updated launcher

Update checks and installation are deferred while Subnautica is running. Launcher updates preserve the user-owned `Data`, `Plugins`, and `Logs` directories.

## Launcher Files and User Data

Keep these release items together:

- `SubnauticaLauncher.exe` — main launcher and isolated plugin host
- `SNLUpdater.exe` — launcher updater
- `Dependencies` — native UI host, assets, DepotDownloader, and required runtime files
- `SNLUpdates.xml` — launcher update history and payload manifest
- `SNLUUpdates.xml` — updater manifest

The launcher creates or maintains:

- `Data/config.json` — launcher preferences and managed-version state
- `Data/Plugins` — plugin state and settings
- `Plugins` — installed plugin assemblies
- `Logs` — runtime and plugin logs

Back up `Data` if you want to preserve your managed-version list and launcher preferences manually.

## Troubleshooting

### A version does not appear

Try:

- reopening the launcher
- checking that the game folder still exists
- checking that the folder contains `Subnautica.exe`
- adding the version again if its folder was moved outside the launcher

### An existing version is not recognized

The launcher identifies supported builds from known build metadata. Make sure you selected the actual game folder and that its files have not been mixed with another Subnautica build.

### A folder name is rejected

Managed folder names cannot be empty, contain invalid Windows filename characters, or use the reserved name `Subnautica`.

### Launch options do not apply

Per-version launch options apply to direct launches. Disable `Force to Launch With Steam` if you want the launcher to pass options directly to `Subnautica.exe`.

### Force to Launch With Steam fails

Check that:

- the selected version is inside a Steam `steamapps/common` library
- Steam's active `Subnautica` folder is already managed by the launcher
- the folder name needed to restore the previous version is not already occupied

### A plugin will not update

Close Subnautica and try again. Plugin replacement is intentionally blocked while the game is running. Plugin errors are shown on the plugin card and additional details may be available under `Logs`.

### A native launcher window is missing

Make sure the complete `Dependencies` folder was extracted next to `SubnauticaLauncher.exe`. Re-extract the release ZIP if any dependency files are missing.

## Development

Build the solution:

```powershell
.\Scripts\Build.ps1
```

Create a clean release package:

```powershell
.\Scripts\Build-ReleasePackage.ps1 -Clean
```

The packaging script recreates the top-level `Release` folder with:

- `SubnauticaLauncher.zip`
- loose launcher and updater executables
- `Dependencies` and `Dependencies.zip`
- launcher and updater XML manifests

Launcher updates use `Updates/SNLUpdates.xml`, and updater updates use `Updates/SNLUUpdates.xml`. Both are LiveSplit-style XML manifests whose payloads are hosted in the repository's `Release` directory.

## Notes

This rebuild is focused on safe Subnautica version management and an extensible plugin-based tool system. Keep managed folders inside the launcher workflow, especially when using Steam-compatible folder switching, so launcher metadata and physical folders remain synchronized.
