# Subnautica + Below Zero Launcher

The Subnautica + Below Zero Launcher is a Windows launcher for installing, organizing, switching, and launching multiple versions of **Subnautica** and **Subnautica: Below Zero** from one place.

It is built for players and speedrunners who want an easier way to:

- keep many versions installed at once
- swap between versions without manually renaming folders
- launch older versions cleanly
- use built-in reset tools and overlays
- manage supported mods
- stay up to date through the launcher itself

Repository:
- https://github.com/ItsFrostyYo/Subnautica-Launcher

## What The Launcher Does

This launcher helps you manage both games without having to manually move folders around every time you want to switch versions.

For Subnautica and Below Zero, it can:

- download supported public versions
- add an already-installed version into the launcher
- keep Subnautica and Below Zero separated correctly
- let you rename versions so your list stays readable
- launch the selected version directly
- close the currently running game and switch to another version
- remove a version from the launcher without deleting the game files
- fully delete a version if you want to clean it up

## What You Need

- Windows 10 or Windows 11
- Steam installed
- enough disk space for the versions you want to keep
- Internet access for downloading versions, updates, or supported mods

## First-Time Setup

1. Download the latest release from GitHub Releases.
2. Extract the zip somewhere you want to keep the launcher.
3. Run `SubnauticaLauncher.exe`.
4. Let the launcher finish its startup checks.

On first launch, the launcher will make sure its required folders and helper files exist. If something is missing, it will set that up for you.

## Main Window Overview

The main window is split into a few simple areas:

- Left sidebar:
  - `Play`
  - `Settings`
  - `Tools`
  - `Launcher Info`
- Center area:
  - your installed Subnautica versions
  - your installed Below Zero versions
- Sidebar status area:
  - the currently selected version
  - the current version status
  - the main `Launch` or `Close Game` button

There is also a `Switch` button next to the selected version display so you can close the currently running game and launch the selected version right away.

## Installing A Version

To install a normal version:

1. Open the launcher.
2. Go to `Play`.
3. Press `Install Version`.
4. Pick the game and version you want.
5. Enter your Steam login when asked.
6. If Steam requests a code, email code, or Steam Guard input, enter it in the launcher prompt.
7. Wait for the install to finish.

After the install finishes, the version should appear in the correct game list.

## Adding An Existing Version

If you already have a version installed manually:

1. Open `Install Version`.
2. Choose `Add Existing Version`.
3. Pick the folder that contains the game exe.
4. Enter the display name you want.
5. Pick the original version from the dropdown.
6. Save it.

The launcher will detect whether the folder is:

- Subnautica
- Below Zero

and it will create the correct launcher info file for that game.

## Launching A Version

To launch a version:

1. Select a version from the Subnautica or Below Zero list.
2. Press `Launch`.

The launcher will:

- close the currently running game if needed
- move the selected version into the active Steam game folder name
- start the correct game exe

If a game is already open, the main button changes to `Close Game`.

## Switching Between Versions

If one version is running and you want another:

1. Select the version you want.
2. Press `Switch`.

The launcher will close the current game and launch the selected one.

## Editing A Version

Each version row has small action buttons on the right:

- folder button: opens that version’s folder
- cog button: opens the edit window

In the edit window you can:

- change the display name
- change the folder name
- delete the version from the launcher
- delete the full game folder
- open the mods window for that version

## Mod Support

The launcher supports managed mod installs for supported versions.

Right now:

- Subnautica 2018 versions support `Speedrun RNG Mod`
- Subnautica 2022 to 2025 versions support `Speedrun RNG Mod 2.0+`
- Below Zero currently has no launcher-managed mods

To install mods:

1. Open `Install Version`.
2. Press `Install Mods`.
3. Choose whether you want to:
   - install a new modded version
   - install a mod into an existing version
4. Pick the game, version, and mod.
5. Finish the install.

The launcher marks modded versions automatically.

It also auto-detects versions that already have:

- `BepInEx`
- plugins inside the `BepInEx\plugins` folder

So if you manually add a modded version, the launcher can still recognize that it is modded.

### Removing Mods

From the version edit window, press `Mods`.

The mods window will tell you if the version has:

- launcher-managed mods
- manually detected plugins
- only BepInEx with no plugin dlls detected

From there you can remove the mod setup and return the version to a clean state.

## Auto Mod Detection

The launcher checks each version folder and can detect:

- whether `BepInEx` is installed
- whether plugin dlls exist
- known supported RNG mod installs
- multiple plugins at once

If a version has BepInEx but no detected plugins, the launcher will still show that BepInEx is installed.

## Updates

The launcher can check for:

- launcher updates
- supported mod updates

Launcher updates are checked on startup.

Mod updates are checked after startup once the launcher finishes loading. If a supported installed mod has a newer version available, the launcher can offer to update it.

When updating supported RNG mods, the launcher preserves these user files:

- `Options.txt`
- `Custom.preset`
- `Custom.SpawnLoc`

## Steam Login And Version Downloads

Version downloads use DepotDownloader through the launcher.

The launcher supports:

- remembered login state
- password entry
- Steam Guard / email code prompts
- retrying code input when Steam asks for it

If Steam asks for a code, the launcher should now show a prompt window for it instead of leaving the install stuck.

## Steam AppID File

For Subnautica installs, the launcher makes sure `steam_appid.txt` exists next to `Subnautica.exe` with the correct app id.

This helps the game launch faster and more reliably when launched directly from the launcher.

## Reset Macros

The launcher includes reset tools for speedrunning.

### Normal Reset Macro

Available for:

- Subnautica
- Below Zero

It helps automate resetting runs depending on the selected gamemode and current game state.

### Explosion Reset Macro

Available for:

- Subnautica

It can automatically reset runs based on explosion timing ranges.

The launcher also includes an explosion overlay and tracking support for that workflow.

## Overlays And Speedrun Tools

The launcher includes optional speedrun-focused tools such as:

- explosion overlay
- speedrun timer
- 100% tracker
- biome tracker
- hardcore save deleter

These can be turned on or off from the launcher settings and tools pages.

## Backgrounds And Launcher Appearance

You can customize the launcher background from the settings page.

The launcher keeps the same style across the main window and helper windows, and it supports both normal window mode and overlay mode.

## Overlay Mode

The launcher can also run in an overlay-style mode for gameplay sessions.

Useful overlay features include:

- startup as normal window or overlay
- adjustable transparency
- hotkey to show or hide the overlay

## Version Status Meanings

The launcher shows status information for versions such as:

- idle
- active version
- launching game
- game running
- closing game

The sidebar shows the selected version separately from the live version status so it is easier to tell what is currently selected versus what is currently running.

## Troubleshooting

### A version does not show up

Try:

- restarting the launcher
- checking that the correct game exe is in the version folder
- checking that the folder was added as the correct game

The launcher now tries to repair mixed version metadata automatically if it finds a Below Zero folder with the wrong launcher info file, or a Subnautica folder with the wrong one.

### A download gets stuck on Steam login

Try:

- waiting a moment for the auth prompt to appear
- entering the requested Steam code in the launcher prompt
- using a normal login once before relying on remembered login

### A modded version is not detected correctly

Make sure the version still contains:

- `BepInEx`
- plugin dlls inside `BepInEx\plugins`

If the version has BepInEx, the launcher should treat it as modded. If BepInEx is removed, it should go back to vanilla.

### A version opens as the wrong game

The launcher keeps Subnautica and Below Zero metadata separate:

- Subnautica uses `Version.info`
- Below Zero uses `BZVersion.info`

If an older broken folder had the wrong file, the launcher should repair it automatically on load.

## Logs

If something goes wrong, check:

- `logs/launcher.log`
- `logs/updater.log`
- the install log shown inside the install window

## Important Notes

- This launcher is Windows-only.
- Some features depend on GitHub being available.
- Version installs depend on Steam login working correctly.
- Reset tools and overlays are automation tools and can still be affected by unusual game behavior or unexpected UI changes.

## Disclaimer

This launcher is built for version management, speedrun tooling, and overlay support. It reads game state where needed for tooling, but it is intended to stay within the launcher’s speedrun-focused utility purpose.
