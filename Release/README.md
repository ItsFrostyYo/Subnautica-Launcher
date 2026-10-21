# Subnautica Launcher

Subnautica Launcher is a Windows launcher for **Subnautica.** It is a Remake of the Discontinued Subnautica Trilogy Launcher, with a Similar Functions and new Plugins system.

Repository:

- [https://github.com/ItsFrostyYo/Subnautica-Launcher](https://github.com/ItsFrostyYo/Subnautica-Launcher)

## Supported Games

The current launcher supports:

- **Subnautica**

It does not currently support **Subnautica: Below Zero** or **Subnautica 2**. Below Zero support is planned for the future.

## What It Does

The launcher can:

- Install Any Public Subnautica Version From Steam.
- Add Existing Supported Steam Builds and potentially other from Launchers.
- Organize/Reorder/Rename Managed Versions in the UI, and Launch or Switch between Versions.
- Save Steam Launch Options Separately for each Version.
- Launch using Steam Directly or using Steam AppID for Faster Launching.
- Supports Launching with the Subnautica Speedrunning Mod.
- Save Subnautica In-Game Settings and Automatically or Manually Copy into Versions for Settings Saving.
- Install and Manage Launcher Plugins like Subnautica Speedrunning Reset Macros.
- Automatic Launcher and Plugins Updating.



## Requirements

- 64-bit Windows 10 or Windows 11
- Steam Installed
- Steam account with Valid Subnautica Ownership (For Installing Versions you cannot have it through Family Share)
- Internet Acces for Installing Versions and Updating the Luancher and Plugins.
- Enough Disk Space for what is wanted.

Release builds are self-contained, so a separate .NET installation is not required.

## First-Time Setup

1. Download the latest `SubnauticaLauncher.zip` from [GitHub Releases](https://github.com/ItsFrostyYo/Subnautica-Launcher/releases).
2. Extract the entire ZIP to a normal folder.
3. Keep `SubnauticaLauncher.exe`, `SNLUpdater.exe`, and the `Dependencies` folder together.
4. Run `SubnauticaLauncher.exe`.

Do not run the launcher from inside the ZIP. The launcher needs write access to its folder so it can save configuration, install plugins, write logs, and update itself.

## Main Window

The Main Navigation on the Left of the Launcher Contains:

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
- the YouTube channel
- the Subnautica speedrunning Discord



## Updates

The launcher checks for launcher updates automatically. Launcher and plugin updates wait until Subnautica is closed.

When an update is available, choose `Later` to continue using the current version or `Update Now` to install it. During an update, the launcher:

1. Makes sure `SNLUpdater.exe` is current.
2. Downloads the launcher files listed in the official update manifest.
3. Closes the launcher and starts SNLUpdater.
4. Replaces launcher files and fully re-extracts the dependency bundle when required.
5. Reopens the updated launcher.

Updates preserve managed versions, launcher preferences, plugin settings, installed plugins, and logs.

### Verify Launcher Integrity

Select `Verify Launcher Integrity` in the top-right title bar and confirm with `Yes` to check the launcher installation against the current official release.

- If every managed file has the expected size and SHA-256 checksum, the launcher reports that all files are present and valid.
- If a file is missing or damaged, the progress window identifies the problem and enters repair mode automatically.
- Repair mode downloads clean launcher files, refreshes the updater when necessary, replaces the dependency bundle, and restarts the launcher.
- If the installed launcher is older than the current integrity manifest, the same process updates it to the current release.

The check covers `SubnauticaLauncher.exe`, `SNLUpdater.exe`, the README, launcher-managed manifests, and every shipped dependency file. It does not inspect Subnautica installations, saved games, user configuration, logs, or third-party plugin files.

Subnautica must be closed before verification or repair begins. An internet connection is required to download the official integrity manifest and any replacement files.

## Launcher Files and User Data

Extract the complete release ZIP into one writable folder. The installed launcher includes:

- `SubnauticaLauncher.exe` — the launcher and isolated plugin host
- `SNLUpdater.exe` — the update and integrity-repair helper
- `Dependencies` — launcher assets, DepotDownloader, the native UI host, and required runtime files
- `Data/SNLUpdates.xml` — launcher update history and launcher payload list
- `Data/SNLUUpdates.xml` — updater release information
- `Data/IntegrityManifest.json` — expected sizes and SHA-256 checksums for managed launcher files
- `README.md` — setup and usage instructions

The launcher also creates and maintains user data:

- `Data/config.json` — preferences and the managed-version list
- `Data/Plugins` — plugin state and plugin settings
- `Plugins` — installed plugin files
- `Logs` — launcher, updater, and plugin logs

The updater replaces only launcher-managed files. It does not delete `Data/config.json`, `Data/Plugins`, `Plugins`, or `Logs`. Back up `Data` if you want a manual copy of your launcher configuration.

## Troubleshooting

### Verify Launcher Integrity cannot start

Check that:

- Subnautica is closed.
- No launcher update or plugin update is already running.
- The computer is connected to the internet and can reach GitHub.
- The launcher folder is writable and is not being run from inside the ZIP.

If verification reports a download or manifest error, try again after confirming GitHub is reachable. The launcher does not report a successful check unless it downloaded the official integrity manifest and checked every listed file.

### Integrity repair fails

Close the launcher, reopen it, and run `Verify Launcher Integrity` again. Make sure antivirus software has not quarantined `SNLUpdater.exe` or files inside `Dependencies`.

If the updater opened but could not finish, read `Logs/SNLUpdater.log` for the affected file and error. Confirm the launcher folder is writable and no launcher or dependency process is still running.

### A launcher update fails

Close Subnautica and retry. If the update still fails, run `Verify Launcher Integrity`. A missing or damaged updater, dependency, or launcher file will be downloaded again automatically.

### A managed version does not appear

Restart the launcher and confirm that the game folder still exists and contains `Subnautica.exe`. If you moved the folder outside the launcher, add the version again from its new location.

### An existing installation is not recognized

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

### A launcher window is missing or does not open

Run `Verify Launcher Integrity`. It checks and repairs the native UI host, launcher assets, and other required dependency files. If verification itself cannot run, extract the complete release ZIP into a new writable folder.

## Notes

Keep managed game folders inside the launcher workflow, especially when using Steam-compatible folder switching. Moving or renaming those folders outside the launcher can leave its saved paths out of sync with the files on disk.
