# HexBot

A Windows screen-region pixel detector for testing your game's visuals and measuring simple triggerbot behavior. It can log when a trigger would fire or send an ordinary left-click to a window you bind. The source is included.

## Run

1. Extract the ZIP into a folder.
2. Run **ColorTriggerLab.exe**. Requires 64-bit Windows and .NET Framework 4.8.
3. Click **Open test scene**, then press **F6**. The app tracks a 120 x 120 region at the scene's center.
4. Watch the preview, matching-pixel count and `would_fire` events. The moving gold target periodically crosses the region.
5. Press **F7** to stop.

Monitoring starts paused. Automatic clicking starts disabled. Editing detector settings pauses monitoring so a run always uses a consistent configuration. Closing the app ends capture, unregisters its hotkeys and closes its test scene.

## Test your own game

1. Run your development build in a window or borderless mode.
2. Focus that window and press **F9**. This binds it and centers the current capture region on its client area.
3. Set the region's **Left**, **Top**, **Width** and **Height**, or use **Draw region**. Coordinates are physical desktop pixels; negative coordinates are supported for monitors left of or above the main display. Width and height are capped at 1024 pixels.
4. Enter an RGB hex color, use **Choose color**, or hover over a representative pixel and press **F8**.
5. Set tolerance, minimum pixel count and timing. Move the tester out of the capture region or minimize it.
6. Press **F6** to start detection. Press **F7** to stop; **Export CSV** stops capture and saves the retained trigger events.

F9 recenters the region, so bind the window before drawing a custom region. Custom regions remain at fixed desktop coordinates if a game window moves. Only the built-in test scene's region follows its window automatically.

### Optional automatic clicking

Check **Send a left-click on trigger**, focus your bound test window, place the pointer inside its client area, and press **F6**. This sends a left-button down/up pair at the pointer's current position. It does not aim or move the pointer. In a game with a captured cursor, the game decides how that input maps to firing.

The bound window must be in the foreground and under the pointer, and the physical left button must be released. Focus loss resets the matching-frame streak. A missing F7 global stop hotkey disables automatic-click mode. If hotkeys are occupied, the footer names them; close the conflicting application and restart the tester.

`click_sent` means Windows accepted both input events, **not** that your game accepted a shot or that an anti-cheat approved them. Games may ignore synthetic input. The app reports an input failure and stops when Windows rejects the pair. Standard Windows integrity-level restrictions apply.

## What the detector measures

For each pixel, all three RGB channel differences must be less than or equal to the configured tolerance. The detector counts matching pixels across the whole selected rectangle. It does not recognize a character, head, team, occlusion or crosshair alignment. It does not require the matching pixels to be connected.

| Setting | Default | Meaning |
| --- | --- | --- |
| Color | `#FFD54A` | Target RGB color |
| RGB tolerance | 18 | Inclusive maximum absolute difference in each channel |
| Minimum pixels | 8 | Total matching pixels needed in one captured frame |
| Stable frames | 2 | Consecutive matching scans before an event |
| Scan interval | 33 ms | Requested timer interval; actual rate is displayed |
| Cooldown | 250 ms | Minimum interval between events |
| Repeat while matched | Off | Off: one event per continuous match; on: repeat after cooldown |

With repetition off, one nonmatching frame clears the presence latch. A later match must stabilize again. Cooldown still applies across separate presences. Starting a new monitoring session resets the timing gate.

The preview refreshes at up to 10 Hz; the detector scans at its actual displayed rate. Windows timer scheduling, capture overhead and rendering affect timing. This is a measurement harness, not a guaranteed high-frequency or real-time input tool.

## Built-in test scene

| Key | Scene action |
| --- | --- |
| Space | Pause/resume target movement; paused target is centered |
| T | Hide/show target |
| D | Add/remove a same-color square inside the scan region |
| R | Reset received-click counter |
| F6 | Start/stop monitoring |
| F7 | Stop monitoring immediately |
| F8 | Sample color under pointer and pause monitoring |
| F9 | Bind active window, center region and pause monitoring |

To demonstrate a false positive, hide the target with T and enable the square with D. The square triggers the same detector because its pixels meet the same rule. The click counter counts all left-clicks received by the scene, including manual clicks; reset it with R before measuring automatic input.

## Event data

The latest 250 events appear in the UI. Up to the latest 10,000 are kept in memory for CSV export. The app does not persist screenshots, contact a server or save logs until you choose Export CSV.

CSV includes UTC timestamps, action, matching-pixel count/fraction, capture-plus-scan duration, eligible-presence duration, region coordinates and all detection/timing settings. `eligible_presence_ms` measures time from the first eligible matching scan to the event. It is **not end-to-end reaction time** from the game's first rendered target frame. With repeat enabled, it measures the age of the same continuous presence at each event.

Use your game's own render/event telemetry and input-handler timestamps to measure render-to-input or render-to-shot latency. Compare false positives with false negatives across UI, particles, lighting, color grading, antialiasing, target sizes and accessibility palettes. Detection by an anti-cheat is a separate outcome that this app cannot infer.

## Vanguard and capture limits

This build has **no Vanguard integration, stealth implementation, bypass, or invisibility claim**. It has not been tested against Vanguard. Riot publicly describes detecting pixel/color bots; using screen pixels instead of reading game memory is not evidence that a program is undetectable. Riot's public materials describe Vanguard as its custom security software. This project does not establish that Vanguard is available for integration into a third-party game.

The app uses documented desktop screen capture (`Graphics.CopyFromScreen`), global hotkeys (`RegisterHotKey`) and standard mouse input (`SendInput`). There are no drivers, process-memory reads, game hooks, modified game files or privileged components.

Desktop capture sees visible pixels. Occlusion, exclusive fullscreen, protected surfaces, HDR/display conversion and overlays can cause missing pixels or changed colors. Inspect the preview and sample from the actual captured presentation. Avoid placing the tester over the region: it pauses capture there to prevent feedback from its own preview. A rectangle spanning empty space between monitors can capture meaningless pixels; prefer a region fully within one display.

For anti-cheat evaluation, retain a transparent test build and a test matrix recording game build, anti-cheat version, display configuration, detector settings, input accepted/rejected and the anti-cheat's actual result. A run without a visible response does not establish invisibility or absence of later enforcement.

References checked November 18, 2026:

- [Riot: Vanguard's detection of pixelbots](https://playvalorant.com/en-gb/news/dev/vanguard-hits-new-bans-per-second-record/)
- [Riot: Vanguard FAQ](https://support.riotgames.com/en-us/league-of-legends/performance/riot-vanguard-faq-league-of-legends)
- [Microsoft: Graphics.CopyFromScreen](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.graphics.copyfromscreen)
- [Microsoft: SendInput and integrity restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [Microsoft: RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)

## Build and test

Run `build.cmd` in this folder. It uses the .NET Framework C# compiler at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` and produces a 64-bit executable. No NuGet packages are used. The source uses C# 5 syntax for compatibility with that compiler.

From PowerShell, run the deterministic tests without starting the GUI:

```powershell
$reportPath = Join-Path $PWD 'test-results.txt'
$testProcess = Start-Process -FilePath '.\ColorTriggerLab.exe' -ArgumentList @('--self-test', ('"' + $reportPath + '"')) -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath $reportPath
$testProcess.ExitCode
```

Tests cover per-channel thresholds, bitmap counting, consecutive-frame requirements, cooldown and repeat behavior, reset behavior, native input layout, unbound-window blocking and locale-independent CSV output. They never send clicks. See `test-results.txt` for the build's recorded results.

Source layout: `Core.cs` contains detection and timing; `Native.cs` contains Windows interop; `MainForm.cs` contains capture/UI; `DemoForm.cs` and `RegionPicker.cs` provide testing and selection; `SelfTests.cs` verifies deterministic behavior.
