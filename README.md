<p align="center">
  <img src="images/icon.png" width="96" height="96" alt="XivType icon">
</p>

<h1 align="center">XivType</h1>

A Dalamud plugin that types the enemy prompts in Keybound Brawler for you. English client only.

If XivType helps you, you can support it on [Ko-fi](https://ko-fi.com/kuchris).

## Install

Add this URL in `/xlsettings` → **Experimental** → **Custom Plugin Repositories**:

```text
https://raw.githubusercontent.com/kuchris/DalamudPlugins/main/repo.json
```

Enable the repository, then search for **XivType** in `/xlplugins`. Enter the activity and press **Start automatic typing** in `/xivtype`.

## Features

- Types with real keystrokes sent only to the FFXIV window, so it keeps working while another app has focus. Opens the input with Enter and never types into chat.
- Targets the shortest prompt or the nearest enemy.
- Pauses for Dynamic Sensory Jammer and Cursed Gaze from half the cast bar, and for the game's own typing lock.
- Retries missed keys and retypes wrong input instead of stopping. Accented letters are typed as plain letters.
- **Game frame rate** section: shows the measured fps and changes the game's frame-rate settings. Typing speed follows the frame rate.

## Commands

| Command | Action |
| --- | --- |
| `/xivtype` | Open the window |
| `/xivtype on` | Start automatic typing |
| `/xivtype once` | Type one prompt |
| `/xivtype off` | Stop |
| `/xivtype status` | Print the current status |

Escape stops typing while FFXIV is focused.

## Settings

- **Typing delay** (default 100 ms): extra wait after each character. At 0 ms it types about one character per frame.
- **Key hold** (default 0 ms = one frame): raise it if keys are missed.
- **Allow background typing** (default on): uncheck to type with the physical keyboard, which needs FFXIV in the foreground.
- **Target priority**: shortest prompt or nearest enemy.
- Diagnostics capture is off by default.

## Build

Requires .NET SDK 10 and Dalamud API 15 development files.

```powershell
dotnet build XivType/XivType.csproj -c Release
dotnet run --project tests/XivType.Replay.csproj
```

Development notes are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md); live test history is in [docs/LIVE-VALIDATION.md](docs/LIVE-VALIDATION.md).
