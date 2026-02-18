# Subnautica + Below Zero Launcher

Advanced Windows Launcher for Installing, Managing, Switching, and Launching many Versions of **Subnautica** and **Subnautica: Below Zero**, with Integrated Speedrun Tooling, Overlays, and Automatic Self-Update Support.

Repository:
- https://github.com/ItsFrostyYo/Subnautica-Launcher

## Quick Start

### 1. Download

From GitHub Releases, download the latest release package zip and extract it.

Recommended package layout after extract:

- `SubnauticaLauncher.exe`
- `SNLUpdater.exe`
- `tools/`

### 2. Run

Start `SubnauticaLauncher.exe`.

On first launch, setup checks required folders/tools and installs missing runtime pieces automatically.

### 3. Add a Version

1. Click `Add` on Version List Tab.
2. Pick a Subnautica or Below Zero version or press "Add Existing Version" if you already have a subnautica version installed.
3. Enter Steam login details (and Steam Guard code if requested).
4. Wait for install to complete.

### 4. Launch a Version

1. Select a version from either game list.
2. Click `Launch`.

The launcher handles active-folder switching for you.

## What This Project Does

This Launcher is an app built for Subnautica Players and Speedrunners and Includes:

- Multi Version Managing.
- Version Launching & Swapping.
- Any Public Version Downloading using DepotDownloader.
- Auto Launcher Update Detection & Downloading.
- Custom Tools for Speedrunning Subnautica & Below Zero.
  - Reset Macros for Any Gamemode, Version & Even Explosion Time.
  - Automatic Hardcore Save Deleters for Deleting unused new Hardcore Saves.
  - Button to delete all Hardcore Saves from Active Version, all Versions & select Subnautica, Below Zero or Both
- Custom Overlays and Trackers for Speedrunning
  - Can Toggle an Overlay to display Explosion Time, Resets per Session and Display for Current Reset Macro Step.
  - Can Toggle Fully Customizable Tracker for the 100% Speedrun, Tracks Blueprints & Databank Entry Unlocks and % to completion of 100%
  - Can Toggle Per Biome Unlocks Display to see what you are missing in the current biome you are in.

Target platform:

- Windows (`net8.0-windows`, WPF).

## Core Features

### Version Management

- Tracks installed managed versions using marker files:
  - `Version.info` for Subnautica
  - `BZVersion.info` for Below Zero
- Supports managed install + unmanaged import.
- Edit display name/folder name, remove from launcher, or delete full game folder.

### Safe Active-Folder Switching

When launching, the launcher:

1. Closes running game processes.
2. Restores current active folder (`Subnautica` / `SubnauticaZero`) back to a managed folder name.
3. Moves selected target folder into active game name.
4. Starts the game executable from the active folder.

This is handled by `Versions/LaunchCoordinator.cs`.

### DepotDownloader Install Flow

- Uses `SteamRE/DepotDownloader` for manifest installs.
- Custom install window streams output, tracks progress, handles auth prompts, and supports cancellation.
- Supports:
  - remembered login (sentry cache)
  - remember-password flag
  - optional prefer-email/2FA input mode (`-no-mobile`)
- Cancelling a fresh install attempt can remove newly created target folders.

Key files:

- `Installer/DepotInstallWorkflow.cs`
- `UI/DepotDownloaderLoginWindow.xaml.cs`
- `UI/DepotDownloaderInstallWindow.xaml.cs`

### Automatic Update System

On startup, launcher checks latest GitHub release (`ItsFrostyYo/Subnautica-Launcher`) and if newer:

1. Verifies/downloads latest `SNLUpdater.exe`.
2. Downloads latest `SubnauticaLauncher.exe`.
3. Launches updater with staged exe + current exe path + current PID.
4. Updater waits for launcher exit, replaces executable with retry logic, relaunches launcher.

Key files:

- `Updates/UpdateChecker.cs`
- `Updates/UpdaterChecker.cs`
- `Updates/UpdateDownloader.cs`
- `Updates/UpdateHelper.cs`
- `tools/SNLUpdater/Program.cs`

### Runtime Bootstrap

At startup, launcher verifies required runtime layout:

- `tools/`
- `data/`
- `logs/`
- `ExplosionResetHelper2018.exe`
- `ExplosionResetHelper2022.exe`
- `DepotDownloader.exe`

If missing, setup/bootstrap restores them.

Key files:

- `Installer/NewInstaller.cs`
- `UI/SetupWindow.xaml.cs`

### Overlay Mode

Launcher can run as a overlay window on the top of the screen:

- Startup mode: Window or Overlay. Choose either to Startup as Launcher Window or Launcher Overlay for Gaming.
- Global Overlay Visability Toggle Hotkey (default: `Ctrl+Shift+Tab`)
- Adjustable overlay Transparency

Key files:

- `UI/LauncherOverlayWindow.xaml.cs`
- `UI/OverlayWindowNative.cs`
- `Enums/LauncherStartupMode.cs`

### Reset Macros

- Standard Reset Macro for both Subnautica and Below Zero, detects Version and Uses Set Gamemode to Reset out of or into a new Save File for Speedrunning.
- Explosion Reset Macro for Subnautica, uses Version Detection and dynamically reads for explosion time, automatically resets bad explosion times until a good one has been found (Set a Range for Good Explosion time)

Key files:

- `Macros/ResetMacroService.cs`
- `Macros/BZResetMacroService.cs`
- `Explosion/ExplosionResetService.cs`
- `Macros/MacroRegistry.cs`
- `Macros/GameStateDetectorRegistry.cs`

### Explosion Reset Overlays and Helpers

- Optional on-game overlay showing explosion timer and reset count.
- Uses helper exes (`ExplosionResetHelper2018.exe`, `ExplosionResetHelper2022.exe`) to skip cutscene/reset flow.
- Optional reset result tracking written to data log.

Key files:

- `Explosion/ExplosionDisplayController.cs`
- `UI/ExplosionResetDisplay.xaml.cs`
- `Explosion/ExplosionResetTracker.cs`

### Gameplay Event Tracking

- Dynamic Event Documenter Tracks, Item Pickups/Drops, Blueprint Unlocks, Databank Entry Unlocks, Biome Swapping, Coordinates, Explosion Time, Specific Run Start Logic, and Writes to (`jsonl`).
- 100% Tracker Uses the Dynamic Event Documenter to accurately Track Gameplay to help Track 100% Speedrun Progression.
- Launcher Tracks all Subnautica, TechTypes, Biomes, Databank Entires, Blueprints, Bluprints and Databank Entries per Biome, and more.

Key files:

- `Gameplay/GameEventDocumenter.cs`
- `Gameplay/Subnautica100TrackerOverlayController.cs`
- `UI/Subnautica100TrackerCustomizeWindow.xaml.cs`
- `Gameplay/SubnauticaBiomeCatalog.cs`
- `Gameplay/SubnauticaUnlockPairingCatelog.cs`

## Settings Reference

Settings file:

- `data/launcher_settings.json`

Key settings (selected):

- `BackgroundPreset`
- `ResetMacroEnabled`
- `ResetHotkey`
- `ResetGameMode`
- `RenameOnCloseEnabled`
- `HardcoreSaveDeleterEnabled`
- `Subnautica100TrackerEnabled`
- `Subnautica100TrackerUnlockPopupEnabled`
- `Subnautica100TrackerSurvivalStartsEnabled` (default `true`)
- `Subnautica100TrackerCreativeStartsEnabled` (default `false`)
- `SubnauticaBiomeTrackerEnabled` (default `true`)
- `ExplosionResetEnabled`
- `ExplosionPreset`
- `ExplosionOverlayEnabled`
- `ExplosionTrackResets`
- `StartupMode`
- `OverlayToggleKey`
- `OverlayToggleModifiers`
- `OverlayPanelOpacity`
- `DepotDownloaderLastUsername`
- `DepotDownloaderRememberPassword`
- `DepotDownloaderUseRememberedLoginOnly`
- `DepotDownloaderPreferTwoFactorCode`
- `DepotDownloaderRememberedLoginSeeded`

## Logging

Global logger:

- `Core/Logger.cs`

Behavior:

- Timestamped log lines
- Thread-safe writes
- Path-safe multi-file logging
- Throttled log helpers

Macro logs are separated by channel:

- Subnautica reset
- Below Zero reset
- Explosion reset

Display logging is cached and only emits on display-change/no-primary-screen conditions to reduce startup/runtime spam.

## Project Structure

Folders:

- `UI/` WPF windows and view logic.
- `Versions/` Subnautica version metadata + launch coordination.
- `BelowZero/` Below Zero equivalents.
- `Installer/` DepotDownloader install pipeline + runtime bootstrap.
- `Updates/` Update check/download/apply workflow.
- `Explosion/` Explosion resolver, presets, overlay, reset service.
- `Macros/` Input automation, game-state detection, reset flows.
- `Gameplay/` Event capture + tracker/biome logic.
- `Settings/` JSON settings model.
- `Core/` app paths and shared logger.
- `scripts/` packaging and utility scripts.
- `tools/` helper executables + updater project.

## Runtime Files and Folders

Runtime root is the launcher executable directory (`AppContext.BaseDirectory`).

Expected folders:

- `tools/`
- `data/`
- `logs/`

Common runtime files:

- `data/launcher_settings.json`
- `data/gameplay_events.jsonl`
- `data/biome_catalog.json`
- `data/biome_unlock_pairing_catalog.json`
- `data/biomes_observed.jsonl`
- `data/LastExplosionReset.log` (if tracking enabled)
- `logs/launcher.log`
- `logs/updater.log` (written by updater)
- `logs/reset-macro/subnautica-reset-macro.log`
- `logs/reset-macro/below-zero-reset-macro.log`
- `logs/reset-macro/explosion-reset-macro.log`

Managed version marker files inside Steam `common` folders:

- `Version.info`
- `BZVersion.info`

## Build and Run (Source)

### Prerequisites

- Windows 10/11
- .NET 8 SDK
- Steam installed (for real install/switch workflows)

## GitHub Release

Auto update logic expects release assets named:

- `SubnauticaLauncher.exe`
- `SNLUpdater.exe`

Repository used by updater:

- Owner: `ItsFrostyYo`
- Repo: `Subnautica-Launcher`
- Endpoint: `/releases/latest`

If GitHub digest metadata is missing for updater asset, launcher intentionally refreshes updater download to avoid stale updater usage.

## Operational Assumptions

- Reset macros use predefined 1920x1080 logical click/pixel profiles and scale them to your primary display.
- Macro state detection relies on game focus + screen pixel sampling.
- Steam library discovery is based on default Steam paths, registry Steam path, and `libraryfolders.vdf`.
- Launcher expects managed version folders to contain valid `Version.info` / `BZVersion.info`.

## Safety and Migration Behavior

- `Installer/OldRemover.cs` migrates legacy settings/files and cleans updater residue files.
- Startup performs runtime verification and repair via `NewInstaller`.
- Update flow fails safe: if update fails, launcher continues startup on current version.
- Many operations are best-effort and non-fatal by design to prevent hard startup failure.

## Troubleshooting

### Update not applying

- Check:
  - `logs/launcher.log`
  - `logs/updater.log`
- Verify Internet Connection or Github Connection

### DepotDownloader install issues

- Confirm Steam credentials and Steam Guard code entry.
- Try disabling "Use remembered login only" for first seeding run.
- Check install window log and `logs/launcher.log`.
- Ensure `tools/DepotDownloader.exe` exists.

### Helpers or tools missing

- Restart launcher and let setup/bootstrap run.
- Or run setup path by deleting missing tool and relaunching.

## Current Limitations

- Windows-only application (`net8.0-windows` WPF).
- Install/update flows depend on GitHub availability and valid release asset naming.
- Reset macros are automation based and can be affected by unexpected UI/game-state changes. Updates to later versions of the game, or going back to Early Access Versions of the Game will break Reset Macros or possibly not allow for Game State Detection.

## Important Disclaimer

This project automates inputs and interactions, does not change or alter game memory only reads it for the use of speedrun tooling, it prevents the output of coordinates for legality of speedrunning. any problems check logs, or reach out to Developers.
