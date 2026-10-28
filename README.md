# XivType

Dalamud API 15 plugin for automatic English typing in Keybound Brawler. Reads active enemy prompts, picks the shortest matching prompt, updates the activity's input component and submits through its game callback. Each edit and submission must be acknowledged by the live game before continuing. Background typing is enabled by default.

## Build and load

1. Run `dotnet build XivType/XivType.csproj -c Debug`.
2. In Dalamud's developer plugin locations, add the absolute path to `XivType/bin/Debug/XivType.dll`.
3. Open `/xlplugins`, go to the developer plugins section, and enable XivType.
4. Enter the activity and run `/xivtype on`, or use the **Start automatic typing** button in `/xivtype`.

## Controls

- `/xivtype`: open the control window.
- Starting automatic typing or typing one prompt keeps the control window open.
- `/xivtype on`: start continuous typing.
- `/xivtype once`: type and submit one prompt.
- `/xivtype off` or `/xivtype stop`: stop typing and diagnostic capture.
- `/xivtype status`: print the current status.
- **Escape while FFXIV is focused**: stop typing immediately. Use the window's X to close it without stopping. Escape in another application does not stop background typing.

**Allow background typing** is enabled by default. Text edits address only `KTGInput`, so typing can continue while another application has focus. The optional activity pulse sends a modifier message only to FFXIV's own window; neither path uses global `SendInput` in background mode. A focused ImGui text field inside FFXIV still pauses typing. A background frame-rate limiter can reduce the update rate.

Uncheck **Allow background typing** to use the older physical-keyboard route. That route requires FFXIV in the foreground and pauses while modifier keys are held. Switching routes stops automation; start again afterward. The plugin starts disabled after every load/reload and stops after logout or leaving the supported activity. It waits through transitions and temporarily disabled activity input fields.

**Send activity key during background typing (experimental)** addresses FFXIV's own window with a 250 ms Shift press at most once every 30 seconds, following confirmed plugin typing. It runs only with background typing enabled, the controller running in territory 1359, recent acknowledged input and permission to type. Jammer/prohibition states pause pulses; stopping, unloading or losing permission releases an owned key. It never steals focus or sends a global keyboard event. The game must report the key as down/pressed within 1.5 seconds or automation stops. A pre-existing held Shift is left alone.

The old timer-reset approach **failed live AFK protection** and has been removed. Version 0.1.14's replacement registered three normal keyboard inputs and natural timer resets during a 63-second unfocused lobby test. This establishes the input path, **not successful duty/server AFK protection**. A complete protected duty run remains unverified; the reported approximately 60-second expulsion threshold is also unconfirmed. No inactivity timer fields or time limits are edited.

## Scope and behavior

- Currently restricted to the English client and the observed activity territory `1359`.
- Multiple enemy prompts can be valid at the same time. **Target priority** chooses among prompts matching existing input: **Shortest prompt** (default) or **Nearest enemy**, measured in world distance from your character. Either can differ from the enemy you are watching.
- Nearest enemy binds each prompt to the targetable enemy whose projected screen position lies directly below its label. Enemies lined up at the same screen X, or one enemy under two labels, stay unbound; unbound prompts sort after bound ones, then by length.
- The input field may normalize case; acknowledgement comparisons ignore case.
- Noise-masked prompts remain eligible when their active container and replacement image tiles are visible, even though the label itself is hidden. Hidden labels on inactive containers are excluded.
- A prohibited-input marker pauses both input routes. While permission is false or the lock countdown is positive, the plugin sends no text edits, clearing, Enter or retries. Existing partial input is preserved and acknowledgement timers pause. It resumes 150 ms after permission returns and the countdown reaches zero.
- All loaded battle NPCs and all visible `KTGEnemyCastBar` action captions are checked for **Dynamic Sensory Jammer**. The UI caption fallback covers casts not reported through actor casting data. Any match pauses all typing, regardless of boss status, current target or which enemy's prompt was selected. The block remains for 750 ms after the cast disappears, then normal permission/settling checks apply. Exact visual-marker timing still needs live verification.
- If the selected prompt disappears, the plugin clears its own partial input directly in background mode, or using Backspace in keyboard mode, and chooses again. Unmatched input already present when starting is left for the user to clear manually.
- An unacknowledged key can be retried once with a longer hold only if the input is unchanged and the prompt remains active. A second timeout or an unexpected input change stops automation. Enter has the same bounded, state-checked recovery.
- Keyboard layout mapping uses the current Windows layout. Unsupported characters stop automation.

The control window offers **Typing delay** (default 10 ms) and **Clear delay** (default 0 ms). Delays are additional waits after game acknowledgement, not a fixed characters-per-second promise. Keyboard mode additionally offers **Key hold** (default 35 ms); Backspace uses a shorter 20 ms hold and each released key has a separate gap before another key. A missed physical key raises the effective hold to 60-80 ms for the current plugin instance. Speed and input-route settings are saved; enabled state and adaptive hold are not saved.

This is a local development build. Live typing, submission, punctuation, masked prompts, background operation and Escape stopping were exercised on 2026-10-28. Japanese input and other game builds have not been validated. See [live validation](docs/LIVE-VALIDATION.md).

## Diagnostics and development

The Diagnostics section can capture visible addon names, activity text, and an explicitly selected addon's full node tree and AtkValues. Files are stored in the plugin configuration directory under `diagnostics/`. A background worker writes managed snapshots; native UI access stays on the framework thread. History rotates at 16 MiB and may drop old queued diagnostic entries if the disk falls behind.

`typing-state.json` contains version, route diagnostics, foreground state, input permission/countdown, read-only inactivity timers, targeted activity keys sent/observed, edit/submission operation counts, global-system-key-event count, status, active prompts, input, score and acknowledgement counters. Local development commands (`probe`, `once`, `on`, `off`) can be written to `diagnostics/control.txt`; reload discards unconsumed commands. `scripts/Invoke-LiveProbe.ps1` runs a bounded live character or single-prompt check. It requires a visible prompt; keyboard mode also requires game focus.

`scripts/Test-BackgroundTyping.ps1` waits for active background typing, then checks that acknowledged submissions increase while every sample remains unfocused and the system-key-event counter stays unchanged. It does not switch windows or emit input itself.

`scripts/Test-ActivityInput.ps1` runs explicit bounded lobby-only `activity-probe` commands while automation is stopped. It checks normal keyboard-state acknowledgement and natural input-timer reset. These probes send a targeted modifier message, without entering a duty or writing timers. They are diagnostic exceptions to the production activity-only scope.

Run `dotnet run --project tests/XivType.Replay.csproj` for the masked-prompt and input-prohibition regressions. These reconstruct captured native UI data in isolated memory and exercise the production reader and permission guard, including hidden-container exclusions, a 7-second lock, permission/countdown transitions and disabled components. They do not inject input or attach to the game.
