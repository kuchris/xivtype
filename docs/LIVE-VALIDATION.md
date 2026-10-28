# Live validation - 2026-10-28 (JST)

Current status: the timer-reset AFK mechanism failed in the live duty and was removed. Version 0.1.14 uses game-targeted keyboard messages, verified locally over 63 seconds. Duty/server AFK prevention is still unverified. Earlier callback/submission and timer-write tests below are historical evidence and must not be read as proof of a complete unattended run.

Environment: English FFXIV client, Keybound Brawler territory 1359, Dalamud API 15, .NET 10. The user entered the activity and supplied manual typing samples. No packet injection or binary patch was used.

## Observed UI contract

- `KTGEWord`: visible enemy components contain a text node with ID 20. Multiple prompts can exist simultaneously.
- `KTGInput`: component node 7 contains text node 16 holding the typed input.
- `KTGHudScore`: AtkValue 1 reports the score.
- `KTGPlayGuide`: explicitly instructs `[Enter]: Submit Text` and `[Esc]: Delete Text/Quit`.

## Input experiments

Window `WM_CHAR` messages were unreliable: an initial `r` was not acknowledged within 1.5 seconds, although a subsequent space probe was accepted. This did not establish whether missing input focus or the message route caused the difference. That implementation was removed.

Physical scan-code events through Windows `SendInput`, held for 45 ms and followed by game-state acknowledgement, successfully completed prompts. Acknowledgements ignore case because the game input can normalize it. One early attempt failed during mixed manual/plugin testing; a clean second session confirmed whole-prompt submission.

Evidence from the second session includes automatically submitted `cloud`, `nidhogg`, `Zangan`, `Zack`, `Wark wark!`, `cripshay`, `Steel Reaper`, and `Oliver's Farm`. At one checkpoint the controller had acknowledged 29 submissions and the displayed score was 12900. Scores can also change because of game mechanics and are not attributed exclusively to the plugin.

## Final build check

Version `0.1.5.0` was built with zero warnings/errors, automatically reloaded by Dalamud, and tested with:

```powershell
.\scripts\Invoke-LiveProbe.ps1 -Mode once -TimeoutSeconds 15
```

Result: `PASS: Prompt submitted and acknowledged. Score: 29791.` Continuous mode was then re-enabled for the ongoing session.

Focus switching paused typing; Escape stopped it. The user confirmed an observed Escape stop was from closing the window and authorized resuming. The final UI closes itself after starting and no longer opens automatically on reload.

One 300 ms framework hitch occurred in the earlier synchronous diagnostic implementation. The final build moves file I/O and development-command polling to a bounded background worker and trims automatic captures to visible text. This is a structural fix; no dedicated frame-time benchmark was run.

## Limits

This is live functional evidence for the observed English activity and current client. It is not validation of Japanese input, every prompt/mechanic, every keyboard layout, other territories, or a complete unattended event run. Partial-input cleanup was observed during live transitions, but all possible teammate/target-disappearance races have not been exhaustively tested. The static REA string query did not complete and supplied no offsets or signatures used by the plugin.

Selected supporting events are retained in `live-events-2026-10-28.log`; current snapshots remain in the local plugin configuration diagnostics directory.

## Noise masking and speed fix - version 0.1.6.0

The user's screenshot showed a noise-covered enemy prompt and the partial input `man`. A retained full capture from 18:32:35.215 shows active component 20004 with hidden label 20 containing `mandragora`, visible content node 4, visible replacement-image container 10, and visible image nodes 17 and 18. The previous reader required the label and all its ancestors to be visible, so the active masked prompt was omitted even though the underlying string remained available. This prevented the captured `man` prefix from matching its prompt.

The same production reader was extracted into `PromptReader.cs`. Before the fix, the captured native-hierarchy replay returned three other prompts and failed with `Active masked mandragora is missing; the existing 'man' prefix cannot continue.` The fix accepts the retained label only when its active component, content container and replacement-image layer remain visible. The replay then returned `20004=mandragora` and passed 8 regression checks, including hidden-component/content/layer and missing-noise-image exclusions.

The live 0.1.6 controller recorded selected prompts with `masked=true` and subsequent acknowledgements for `snailshell bracelet`, `grashtrike`, `trypapolis`, `Cetran bracer`, `Slum Wisdom`, `metal knuckles`, `Fortune Telling`, `twin brain`, `elder golem`, and `planetarium`. These verify the masked reader path in the live game; the complete screenshot-specific boss segment is tracked separately below.

Timing observations from the same ongoing live session: ordinary intra-prompt character-send intervals below 250 ms had a median of 132 ms before the speed change (340 intervals), versus 83 ms after it (457 intervals). This is about 1.6 times the character throughput in these samples, not a whole-run benchmark. Defaults are 10 ms extra typing delay, 0 ms extra clearing delay and a 35 ms key hold. Backspace holds for 20 ms. Every key still waits for game acknowledgement, and a separate key-up gap protects repeated characters.

At a later 0.1.6 checkpoint the controller had acknowledged 108 submissions in that loaded instance. A subsequent character timeout expected `alexan` but observed `alexa`; automation stopped as designed. Version 0.1.7 adds one state-checked retry when the input is exactly unchanged and the target is still active, using a longer effective hold of 60-80 ms. Unexpected input changes and a second timeout still stop automation. Enter recovery likewise requires the complete unchanged target input and an active target, and is limited to one retry.

The final 0.1.7 build and the 8 masked-capture replay checks pass. The build was loaded by Dalamud. The run ended before a new functional probe could complete; leaving territory 1359 stopped automation. The retry branch has not been deliberately induced in the live game, and the screenshot-specific boss segment has not been re-observed after the fix. These remain runtime verification limits; the captured original failure and the other live masked submissions establish the reader fix separately.

## Background input - version 0.1.9.0

The foreground restriction in keyboard mode caused the user's Alt-Tab failure. The replacement background route validates component 7 as a native `TextInput`, verifies its owner is `KTGInput` and checks that its input callback exists. On the framework thread, it uses the component's `SetText` method and `TextChanged` callback for edits, and its `Enter` callback for submission. It honors the game's `ClearText` callback result and waits while the activity disables its input. The existing game acknowledgements and territory/language restrictions remain in use.

The route is independent of OS keyboard injection. Escape only stops automation when FFXIV is the foreground application, so Escape in another application does not interrupt background typing. Foreground keyboard mode remains available by disabling Allow background typing.

The 0.1.8 character probe passed and native submissions were observed. Version 0.1.9 adds disabled-input waiting and a system-key-event counter. It built with zero warnings/errors, loaded successfully, and passed:

```powershell
.\scripts\Test-BackgroundTyping.ps1 -Seconds 8 -WaitSeconds 45 -OutputPath docs\background-validation-2026-10-28.json
```

Result: `PASS: 7 acknowledged submissions while continuously unfocused; score 26370 -> 29170.` Every sampled foreground flag was false and the system-key-event counter increased by zero. The user also confirmed `ok it work`. The complete bounded sample is retained in `background-validation-2026-10-28.json`.

## Prohibited-input marker - version 0.1.10.0

The user identified the red crossed-out keyboard/hands marker during Dynamic Sensory Jammer. A retained KTGInput capture at 18:46:50.081 has AtkValue 2 = false, AtkValue 4 = 7, visible countdown text `7`, and partial input `alexa`. Subsequent captures count down toward zero; the 18:47:00.014 capture has permission true and countdown zero. These data also place the earlier `alexan` acknowledgement failure inside an explicit input-prohibition interval, so that particular failure should be treated as a gameplay lock rather than evidence of a missed physical key.

`InputAvailability` now requires valid permission data, a true permission flag, zero countdown and an enabled native component. A common controller guard precedes acknowledgements, retries, clearing, selection and sending for both routes. It releases any previously held physical key, preserves pending state and text, freezes acknowledgement timeouts, and waits 150 ms after permission returns. Native edit/submission entry points and physical key-down entry points independently recheck permission.

The captured 7-second lock test failed before the fix because an enabled component alone permitted input. After the fix, the same captured data correctly blocks it. Checks also cover unlock resumption, disabled components, positive countdown with a temporarily true flag, zero countdown with permission still false, and unavailable permission data. The existing 8 masked-prompt checks still pass. The final build has zero warnings/errors and was hot-reloaded.

The plugin exposes `InputAllowed`, `InputLockSeconds`, `InputBlockReason`, `EditOperations` and `SubmitOperations` for a future live marker interval check. This change is verified by captured-data replay and build/load checks; the marker has not been re-observed live since this version was installed.

## Inactivity expulsion and AFK protection - version 0.1.11.0

The user's second character was expelled at 19:08:42.173 with the exact system message `You were expelled from the duty due to extended inactivity.` Version 0.1.10 had recorded 178 acknowledged submissions before leaving the territory. This invalidates any inference that successful background callbacks also establish normal input activity or prove a complete unattended run.

The current client exposes `InputTimerModule.AfkTimer`, `ContentInputTimer` and `InputTimer` separately from typing callbacks. Version 0.1.11 synchronizes those counters to zero only after acknowledged plugin input, at most every 5 seconds, with protection enabled, the controller running, typing permitted, and both the client and timer module in territory 1359. The native component's time limits, status and history arrays remain unchanged. Missing/non-finite data or a territory mismatch stops typing rather than silently continuing without protection.

The regression first failed because acknowledged input left native-structure counters unchanged. It then passed with real production synchronization code, including disabled/stopped/prohibited/outside-territory guards, stale-module and missing-module rejection, non-finite-data rejection, rate limiting, and preservation of the inactivity limit. Existing prompt and prohibition regressions also pass. The plugin built with zero warnings/errors and was loaded in the client.

Read-only client telemetry in the lobby showed timer module territory 1360, InputTimer about 10.58 seconds, and InstanceContentAfkTimeLimit 600 seconds; no protection writes occur in the lobby. This confirms module access, not successful live protection. A full live activity run with refresh telemetry and no inactivity expulsion remains unverified. Automatic typing remains off after this installation.

## Jammer casts on small enemies - version 0.1.12.0

The user clarified that the no-typing marker also appears on ordinary enemies. The previous guard only observed the player's already-prohibited input state; it did not identify dangerous enemy casts in advance.

Read-only inspection of this installed client's Action sheet found Keybound-era Dynamic Sensory Jammer rows 49532 and 49702, each with Cast100ms = 50 and EffectRange = 70. The controller now scans every loaded battle NPC for either active cast, without a boss, actor-name or current-target filter. Any match pauses both routes, edit/Enter retries, cleanup and AFK synchronization through the common permission guard. It holds the block for 750 ms after a cast disappears; the existing 150 ms settling delay and the player's permission/countdown checks still apply. Telemetry adds EnemyJammerActive and EnemyJammerAction.

Tests cover both IDs on a non-selected enemy, ordinary unrelated casts, stale non-casting action IDs, settling-delay expiry and leaving the activity. The plugin builds and loads; cast IDs and exact visual-marker overlap have not yet been observed in a live run, so cast-based prevention remains a conservative, unverified-in-game mapping to the user's marker.

The same asset inspection found that KTGEWord nodes 17/18 use KTGWordNoise.tex, while 11-16 use aura textures. Noise fallback was narrowed to 17/18. A regression enabling an aura while hiding both noise images failed before this refinement and passes afterward. Earlier masked=true live events from the broad 11-18 check cannot all be attributed specifically to noise; the original retained mandragora noise fixture remains a valid replay case.

The read-only inspection utility is tools/UiInspect. Its extracted UI metadata is under docs/ui-inspection; it does not change the game or plugin state.

## Repeated AFK failure and normal-input replacement - version 0.1.14.0

At 19:21:54.641 the client again reported inactivity expulsion. Seven timer resets had been logged since the preceding reload, including one at 19:21:53.119. The logged content timer was already 0.00 before those resets. This disproves the old protection; writing those fields did not prevent the expulsion. The exact duty/server detector remains unidentified. The user's approximately 60-second timing is a report, not a measured threshold, and the visible 600-second client limit cannot establish this duty's actual behavior.

Timer writes were removed. A bounded lobby probe posted Shift key-down/up messages to the hosting FFXIV window only. With the game unfocused, UIInputData reported `Down, Pressed`, KeyboardInputsChanged became true, and InputTimer naturally fell from about 23.9 seconds to zero without any timer writes. The production replacement uses this same TargetedActivityInput adapter. It schedules an owned 250 ms Shift pulse no more than once every 30 seconds after confirmed typing, subject to running/activity/permission/jammer guards. Unobserved or rejected activity input stops automation; pre-held keys are not taken over.

The shared scheduler tests cover waiting for a typing acknowledgement, key-down/hold/release observation, interval limits, inactive/disabled/prohibited/outside-territory cases, releasing on prohibition, rejecting unobserved input, and preserving a pre-existing held key. Existing prompt and lock regressions still pass; the build has zero warnings/errors.

The final unfocused lobby sequence passed all three probes over 63.32 seconds. Before-input timer samples were 37.01, 29.66 and 29.42 seconds, and each probe observed the normal key state and natural reset. Full samples are retained in `activity-input-sequence-2026-10-28.json`. The lobby test did not enter a duty and cannot verify server/duty AFK immunity. No further risky duty entry was requested. Automatic typing is off after installation.

## Jammer detection failure and visible castbar fallback - version 0.1.17.0

The user's later live run still incurred typing prohibition at 19:43:33.910 and 19:44:17.075. The actor-based proactive jammer detector did not report a jammer before those 7-second locks. This invalidates treating the actor-only check as complete coverage.

Read-only live capture of `KTGEnemyCastBar` showed actual skill captions at text node 4 (e.g. `Burst` and `Tankbuster`). The first static-layout interpretation used node 3 and did not read those captions; changing the regression layout to the observed node 4 reproduced that failure. The corrected reader traverses every visible castbar component, including unselected enemies, and matches `Dynamic Sensory Jammer` by the displayed English action caption. A UI match feeds the existing global input/jammer guard even if actor cast information is absent.

The regression is explicitly a synthetic native hierarchy from shipped ULD structure and observed caption-node placement, not a claimed live jammer capture. It verifies unselected-enemy caption detection, actor-data absence, and hidden stale caption exclusion. The corrected live reader returned `Tankbuster` from the ongoing game, proving ordinary caption extraction. Full live jammer blocking/resumption verification is tracked by `scripts/Test-JammerPause.ps1` and its result file; no pass is implied merely by compilation or caption extraction.

## Nearest-enemy priority - versions 0.1.18.0 and 0.1.19.0

Version 0.1.18 bound prompts to actors from `KTGEWord` PreRefresh/PostRefresh events (AtkValue 0 = command, AtkValue 1 = entity ID). Live logs from 20:06 show every command-2 event leaving the prompt text unchanged (`changed=0`), so no prompt was ever bound, `MappedActors` stayed 0 and Nearest enemy silently fell back to shortest-prompt ordering. A word-array scan for actor IDs and prompt strings also found no matches. The refresh binding was removed.

Version 0.1.19 projects every targetable, living battle NPC with `WorldToScreen` and binds a prompt to the actor directly below its label (horizontal offset at most 48 px scaled, runner-up at least 12 px farther, one prompt per actor). The replay covers binding, world distance, depth-stacked ambiguity, actors above the label and contested actors.

A passive 30-second live sample at 20:22-20:23 (`Test-NearestEnemy.ps1`, no input sent) bound 255 of 295 prompt observations (86%). Bound actors were 0-3 px horizontally from the label centre, with the label top about 170-210 px above the projected position for enemies at 25-50 yalms, so the 48 px tolerance is generous rather than limiting. Unbound observations were enemies at identical screen X (deliberately refused) and brief moments when the label's actor was not projected. Example bindings: `marjoram` 25.8 y, `Psych!` 28.2 y, `Shinra-8` 29.2 y, `nidhogg` 44.7 y, `Stamp` 50.8 y. The raw sample file was overwritten by the following run; the figures above come from analysis of it before that happened.

`Test-NearestEnemy.ps1 -TypeOnce` then observed 13/13 bound prompts, and the single typed and acknowledged prompt was `elemental`, the nearest bound enemy beforehand. The user's continuous run in the same session logged Nearest enemy selections with distances. This verifies binding and selection in this arena layout and camera; other camera angles, very close enemies and overlapping crowds were not specifically tested.

## Cursed Gaze and jammer lock timing - 2026-10-28 20:24-20:26

The user's screenshot shows the prohibited-input marker over an enemy casting **Cursed Gaze** (prompt `hi-ether`). Only Dynamic Sensory Jammer was previously associated with the marker. A timeline of `KTGEnemyCastBar` captions against `KTGInput` permission (AtkValue 2) and countdown (AtkValue 4) in the activity history shows:

| Cast | Visible | Input during cast | Lock |
| --- | --- | --- | --- |
| Cursed Gaze | 19:53:26.4-19:53:30.9 | manual typing (`Farm Bo`) | none |
| Cursed Gaze | 19:54:04.5-19:54:09.0 | manual typing (`EXP U`, `G`) | none |
| Cursed Gaze | 20:24:56.2-20:25:00.5 | plugin typing | 7 s from 20:25:01.7 |
| Cursed Gaze | 20:25:34.2-20:25:38.7 | plugin typing | 7 s from 20:25:39.7 |
| Dynamic Sensory Jammer | 20:26:12.7-20:26:16.9 | none: plugin paused, input held at `P` | 7 s from 20:26:18.4 |

Every lock started about 1 s after the cast caption disappeared, and the existing permission guard paused and resumed typing each time. The jammer row is the first live proactive detection: the plugin paused at 20:26:12.4 before the castbar caption appeared, yet the lock still followed, so pausing during a jammer cast does not prevent its lock. Typing alone does not explain the Cursed Gaze locks, because manual typing through two earlier gazes caused none. A gaze-style facing check is plausible but unverified; character facing was not recorded. Cursed Gaze was therefore not added to the pre-emptive pause list.

## Cursed Gaze pause and scale fallback - versions 0.1.20.0 and 0.1.21.0

The user confirmed that stopping typing during Cursed Gaze avoids its lock, which matches the earlier manual casts that produced none. The installed client's Action sheet has a Keybound-era Cursed Gaze row 49529 (5 s cast, range 70, the same shape as jammer rows 49532/49702). Version 0.1.20 pauses on that action ID or on a visible `Cursed Gaze` castbar caption, through the same guard and 750 ms tail as the jammer, and names the cast in the pause reason. Replay checks cover the ID and caption mapping. The pause has not yet been observed against a live Cursed Gaze.

The user then reported a near enemy (`You should get yourself a reading!`) losing to the distant boss (`crimsonite crystal`) in Nearest enemy mode. The log shows both prompts unbound at 20:28:08 (`distance=unknown`), so the selection fell back to shortest prompt. The live state also showed two enemies projecting to the same screen column (X 1720, the screen centre), which the screen-X binding deliberately refuses to resolve.

A 25-second live sample of bound prompts showed `KTGEWord` label ScaleX decreasing linearly with world distance, from 1.50 at 12.7 y to 1.14 at about 24 y, and holding at 1.14 beyond that (28-40 y). Version 0.1.21 uses that relation as a distance estimate for unbound labels with scale above 1.15; saturated unbound labels still sort last. The calibration reflects this client's UI scale. The reported scene has not been re-run on 0.1.21.

## F13 as the activity key - versions 0.1.23.0 and 0.1.24.0 (lobby only)

The user asked whether a less intrusive key than Shift could serve the activity pulse, since FFXIV briefly sees a real Shift hold. Version 0.1.23 lets the lobby probe post F13 (VK 0x7C, scan 0x64) to FFXIV's own window through the same targeted-message path; production still uses Shift.

The first F13 probe (20:37) reset the idle timer from 12.9 s to 0 about 125 ms after the message, with the game unfocused and `KeyboardInputsChanged` true, but `UIInputData.GetKeyState(0x7C)` never reported F13 down. FFXIV's key-state table evidently does not track F13. Version 0.1.24 therefore also accepts `KeyboardInputsChanged` as the probe's keyboard observation.

The repeated sequence (`activity-input-f13-2026-10-28.json`) gave two clean unfocused passes: 26.0 s -> 0 at 125 ms (20:38:37) and 30.0 s -> 0 at 110 ms (20:39:07). The third pulse is invalid as evidence because the game was in the foreground with a 3.8 s idle timer, meaning the user had just interacted. Together with the first probe, F13 produced three clean natural idle resets. As with Shift, this is lobby evidence only, not duty/server AFK protection. Switching production to F13 would also need the keepalive's acknowledgement to rely on the keyboard-changed flag and idle reset rather than key state.

## Activity pulse interval - version 0.1.25.0

At the user's request the production Shift pulse interval was shortened from 30 s to 5 s (`ActivityKeepAlive.PulseIntervalMs`). The 30 s recency requirement for acknowledged typing, the 250 ms hold and the 1.5 s observation deadline are unchanged. The scheduler replay now also checks that a second pulse follows after 5 s. Not yet observed live at this interval.

## Activity key with the game sent to the desktop - version 0.1.26.0

The user was concerned that the activity key may fail after switching to the desktop. Earlier Shift/F13 evidence covered only an unfocused but visible window. Version 0.1.26 records `IsIconic` in the lobby probe so a truly minimized window can be tested there.

The user pressed Win+D while in the activity (territory 1359), so the lobby probe could not run; production continuous typing was observed instead (20:42:56-20:43:08). Windows reported the FFXIV window as not minimized (`IsIconic` false) throughout, and the game was unfocused until 20:43:04.5. Activity keys were acknowledged at 20:42:57.3, 20:43:02.4 and 20:43:07.3, confirming the 5 s interval live, and typing submissions continued. The run stopped at 20:43:08.3 with `Input changed unexpectedly` shortly after the game regained focus, consistent with manual input. In-activity `InputTimer` read 0.00 s before every pulse, so it cannot show whether a pulse reset idleness there. A window that Windows actually reports as minimized remains untested.
