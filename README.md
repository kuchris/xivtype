<p align="center">
  <img src="images/icon.png" width="96" height="96" alt="XivType icon">
</p>

<h1 align="center">XivType</h1>

A Dalamud plugin that types the enemy prompts in Keybound Brawler for you. Works with the English and Japanese clients.

> [!WARNING]
> This plugin is for educational purposes only. Use it at your own risk.
>
> For normal use, keep **Typing delay** at **100 ms**. Only use **0 ms** if you have a full party.

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
- Japanese client: types each enemy's hiragana reading. It sends romaji keystrokes and then writes the kana, because background keys cannot go through the IME.
- The window opens by itself when you enter the activity. Its language can be English, Japanese or Traditional Chinese (Auto follows the game client).
- **Game frame rate** section: shows the measured fps and changes the game's frame-rate settings. A higher frame rate makes typing faster, because each character takes about one or two frames on top of the delay (about 33 ms per character at 60 fps, 17 ms at 120 fps).
- **Loop**: stand next to the Keybound Registrar, set the number of runs and press **Start loop**. It registers, commences Duty Ready, types each run and exits the results screen.
- **Chat window**: a read-only copy of chat opens during the activity, which hides the game's chat. The game blocks sending chat there.

## Commands

| Command | Action |
| --- | --- |
| `/xivtype` | Open the window |
| `/xivtype on` | Start automatic typing |
| `/xivtype once` | Type one prompt |
| `/xivtype off` | Stop (also stops the loop) |
| `/xivtype loop 5` | Loop 5 runs (`/xivtype loop off` to stop) |
| `/xivtype status` | Print the current status |

Escape stops typing while FFXIV is focused.

## Settings

- **Typing delay** (default 100 ms): extra wait after each character. Keep 100 ms for normal use; at 0 ms it types about one character per frame, so only use 0 ms with a full party.
- **Key hold** (default 0 ms = one frame): raise it if keys are missed.
- **Game frame rate**: higher fps shortens the time per character and between prompts; "Main display refresh rate" gave 119 fps on a 120 Hz display.
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

If XivType helps you, you can support it on [Ko-fi](https://ko-fi.com/kuchris).
