# Subnautica Trilogy Launcher

Subnautica Trilogy Launcher is a Windows launcher for **Subnautica**, **Subnautica: Below Zero**, and **Subnautica 2**.

It is built for players who want to keep versions organized, switch games quickly, launch directly without Steam friction, and use built-in speedrun tools from one place.

Repository:
- https://github.com/ItsFrostyYo/Subnautica-Launcher

## What It Does

The launcher is designed to make multi-version management easier across all supported games.

It can:

- install supported versions
- add existing installs into the launcher
- launch versions directly from their own folders
- switch between versions without manual folder work
- keep Steam-sensitive folders protected
- save per-version launch options such as `-novr`
- manage supported mod installs
- provide reset tools, overlays, timers, and tracker utilities
- update the launcher through its built-in updater flow

## Requirements

- Windows 10 or Windows 11
- Steam installed
- enough free disk space for the versions you want to keep
- internet access for downloads, updates, and supported mod bundles

## First-Time Setup

1. Download the latest release from GitHub Releases.
2. Extract the release zip where you want to keep the launcher.
3. Run `SubnauticaLauncher.exe`.
4. Let the launcher finish its startup/setup checks.

If required runtime pieces are missing, the launcher will set them up automatically before normal startup continues.

## Main Window

The main window is split into:

- `Play`
- `Settings`
- `Tools`
- `Launcher Info`

The left sidebar shows:

- the selected version
- the current live status
- `Launch`
- `Switch`

The Play tab uses configurable game slots instead of being hardwired to just two games. You can choose what appears in `Game 1` and `Game 2`, and you can also switch between labeled and unlabeled list grouping.

## Play Tab

The Play tab is where you:

- browse installed versions
- launch a selected version
- switch from a running version to another one
- open the install folder
- open the edit window
- install new versions

### Game Slots

At the top of the Play tab, you can choose:

- `Game 1`
- `Game 2`
- `List View`

`Game 1` and `Game 2` can be set independently to:

- `Subnautica`
- `Below Zero`
- `Subnautica 2`
- `None`

`List View` can be:

- `Labeled`
- `No Labels`

This lets you focus the main launcher view on the games you actually want to see.

## Installing a Version

To install a version:

1. Open `Play`.
2. Press `Install Version`.
3. Choose the game from the dropdown.
4. Choose the version you want from the list.
5. Choose the Steam library/common path you want to install into.
6. Enter your Steam login when prompted.
7. Complete any Steam Guard or email code prompts if Steam asks for them.
8. Wait for the install to finish.

The install window uses a single game selector and one version list, so the workflow stays the same across all supported games.

## Adding an Existing Version

If you already have a game version installed manually:

1. Open `Install Version`.
2. Press `Add Existing Version`.
3. Browse to the folder that contains the game executable.
4. Let the launcher auto-detect the version from the game files.
5. Enter the display name you want.
6. Save it.

The launcher detects the correct game automatically from the executable in the selected folder and writes the correct launcher metadata for that game.

It also blocks reserved active folder names so you do not accidentally corrupt the live game folders used by Steam or launcher switching.

## Launching a Version

To launch a version:

1. Select it in the Play tab.
2. Press `Launch`.

The launcher will:

- launch the game directly from that version's own folder
- apply saved per-version launch options
- keep Steam-sensitive folder behavior handled automatically

If a supported game is already running, the main button changes to `Close Game`.

## Switching Between Versions

If one version is running and you want to move straight to another:

1. Select the version you want.
2. Press `Switch`.

The launcher will close the currently running game, wait for the handoff to settle, and then launch the selected version.

## Editing a Version

Each version row includes action buttons for:

- opening the version folder
- opening the edit window

Inside the edit window you can:

- rename the display name
- rename the managed folder name
- set per-version launch options
- detect Steam launch options for that game
- open the mods window
- remove the version from the launcher
- delete the full version folder

### Launch Options

Launch options are saved per version and are used when the launcher starts the game directly.

Examples:

- `-novr`
- `-high`

There is also a `Detect Steam Launch Options` button that reads the current Steam launch options for the correct game app id and copies them in for that version.

## Mod Support

The launcher supports managed mod installs for supported game/version combinations.

Current managed support:

- Subnautica 2018 builds: `Speedrun RNG Mod`
- Subnautica 2022-2025 builds: `Speedrun RNG Mod 2.0+`
- Subnautica 2 builds: `Kallie's Command Enabler Mod` (installs and enables both `SN2 Commands Enabler Mod` and `Kallie's Custom SN2 Commands`)

Managed install notes:

- Subnautica 1 managed bundles do not ship a prebuilt `BepInEx\config\BepInEx.cfg`; BepInEx generates the user's config on first game launch.
- Subnautica 2 managed installs use `Subnautica2\Binaries\Win64\ue4ss\Mods` as the working mod root and update `mods.txt` / `mods.json` there automatically.

The launcher can also detect:

- `BepInEx`
- `UE4SS`
- installed plugin folders
- known supported managed installs
- unknown/manual plugin setups

### Installing Mods

To install mods:

1. Open `Install Version`.
2. Press `Install Mods`.
3. Choose whether you want to:
   - install a new modded version
   - install a mod into an existing version
4. Pick the game, version, and mod.
5. Finish the install.

The mods window includes the same game selector flow as the rest of the launcher, so the workflow stays aligned across supported games.

### Mod Update Behavior

When updating supported managed mods, the launcher preserves:

- `Options.txt`
- `Custom.preset`
- `Custom.SpawnLoc`

It also removes stale preset files that no longer belong to the current mod bundle.

## Tools Tab

The Tools tab uses a game-filter chip bar at the top.

You can:

- remove games from the visible filter
- press `+` to add games back
- show only the tools relevant to the games you care about

This keeps the tools page cleaner as more games are supported.

### Tool Availability

Subnautica supports:

- `Reset Macro`
- `Reset Until Explosion`
- `Hardcore Save Deleter`
- `Trackers and Timers`

Below Zero supports:

- `Reset Macro`
- `Hardcore Save Deleter`

Subnautica 2 supports:

- `Reset Macro`

## Reset Macros

The launcher includes reset automation for supported games.

### Reset Macro

Available for:

- Subnautica
- Below Zero
- Subnautica 2

This handles reset flow based on the selected gamemode and current detected game state.

Subnautica 2 currently supports:

- `Survival`
- `Creative`

The Subnautica 2 reset path uses bundled `uhara10` Unreal event memory reading, handles quit-to-menu recovery, and drives the new-game flow through in-game UI event watchers instead of log polling. The launcher self-seeds `tools\uhara10` when needed, while the supporting `LiveSplit` / `SharpDisasm` dependencies are embedded into the launcher itself.

### Explosion Reset

Available for:

- Subnautica

This supports:

- built-in preset ranges
- saved custom min/max time windows
- live range updates while the macro is already running

## Windowed Resolution Support

Subnautica reset macro windowed-mode support is currently tuned for these resolutions:

- 3840x2160
- 2560x1440
- 1920x1080
- 1760x990
- 1600x900
- 1366x768
- 1280x720
- 1128x634

Fullscreen remains the safest option for Subnautica reset automation.

## Hardcore Save Deleter

The launcher includes a Hardcore Save Deleter for supported games.

It can:

- automatically delete the latest qualifying hardcore save after a reset flow
- manually delete hardcore saves from the tools UI

It is wired through the correct save-folder detection for Subnautica and Below Zero and only targets the intended hardcore save behavior instead of wiping unrelated saves.

## Trackers and Timers

Subnautica tools include:

- 100% tracker
- biome tracker
- speedrun timer
- explosion overlay

These can be configured from the launcher and used as part of a normal speedrun workflow.

## Steam AppID Handling

The launcher ensures `steam_appid.txt` is correct for the game being launched directly:

- Subnautica: `264710`
- Below Zero: `848450`
- Subnautica 2: `1962700`

This helps direct-launch behavior stay consistent outside Steam.

## Updates

The launcher can check for:

- launcher updates
- supported mod updates

Launcher updates are checked first. Mod update checks run after launcher startup is complete and only when it is safe to do so.

### Update Flow

The launcher update flow clearly shows both stages:

1. the launcher-side update window while the download and handoff happen
2. the updater window while the launcher is closed, files are replaced, and the new launcher is reopened

So the update process stays visible all the way through close, replace, and relaunch.

## Background and UI

The launcher supports:

- custom backgrounds
- normal window startup
- overlay startup mode
- adjustable overlay opacity
- configurable overlay toggle hotkey

The newer game-selection layout is also built so the launcher can continue scaling cleanly as more game support is added.

## Troubleshooting

### A version does not appear

Try:

- reopening the launcher
- making sure the folder contains the correct game executable
- checking that the version has the correct launcher metadata file
- checking that the folder is inside a detected Steam `steamapps/common` location if you expect it to behave like a managed install

### Add Existing Version is blocked

This usually means one of these:

- the folder does not contain a supported game executable
- the version could not be matched exactly from its build files
- the folder is already managed by the launcher

### Launch options are not applying

Launch options are only applied when the launcher starts the game directly. If you launch the game through Steam instead, Steam's own launch handling applies.

### A folder name is blocked

Certain folder names are reserved for the live active game folders used by the launcher and Steam behavior. The launcher blocks those names intentionally to prevent metadata drift, bad switching behavior, or accidental corruption of active folders.

## Notes

This launcher is designed around direct version management and safe multi-version workflows. If you keep everything inside the launcher flow, it handles far more of the folder, launch, update, and metadata work for you automatically.
