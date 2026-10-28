# Development notes

Automatic diagnostic capture is **off by default** and can be enabled in the Diagnostics section. It captures visible addon names, activity text, and an explicitly selected addon's full node tree and AtkValues. Files are stored in the plugin configuration directory under `diagnostics/`. A background worker writes managed snapshots; native UI access stays on the framework thread. History rotates at 16 MiB and may drop old queued diagnostic entries if the disk falls behind.

`typing-state.json` contains version, route diagnostics, foreground state, input permission/countdown, read-only inactivity timers, edit/submission operation counts, global-system-key-event count, status, active prompts, input, score and acknowledgement counters. Local development commands (`probe`, `once`, `on`, `off`) can be written to `diagnostics/control.txt`; reload discards unconsumed commands. `scripts/Invoke-LiveProbe.ps1` runs a bounded live character or single-prompt check. It requires a visible prompt; keyboard mode also requires game focus.

`scripts/Test-BackgroundTyping.ps1` waits for active background typing, then checks that acknowledged submissions increase while every sample remains unfocused and the system-key-event counter stays unchanged. It does not switch windows or emit input itself.

`scripts/Test-ActivityInput.ps1` runs explicit bounded lobby-only `activity-probe` commands while automation is stopped. It checks normal keyboard-state acknowledgement and natural input-timer reset. These probes send a targeted modifier message, without entering a duty or writing timers. They are diagnostic exceptions to the production activity-only scope.

Run `dotnet run --project tests/XivType.Replay.csproj` for the masked-prompt and input-prohibition regressions. These reconstruct captured native UI data in isolated memory and exercise the production reader and permission guard, including hidden-container exclusions, a 7-second lock, permission/countdown transitions and disabled components. They do not inject input or attach to the game.

Additional control commands: `fps <0-4>` sets the game's frame-rate threshold (2 = 60 fps, 1 = main display refresh rate); `activity-probe` / `activity-probe f13` run the lobby activity-key probe. `scripts/Test-NearestEnemy.ps1` samples prompt-to-enemy binding (`-TypeOnce` types one prompt) and `scripts/Test-JammerPause.ps1` checks that a jammer pauses input and typing resumes.

Release builds: `tools/Build-Release.ps1` builds Release, runs the replay checks, verifies the packaged zip and writes `artifacts/SHA256SUMS.txt`. The icon is drawn by `tools/draw_icon.py`.

Live test history and the reasons behind each design choice are in [LIVE-VALIDATION.md](LIVE-VALIDATION.md).
