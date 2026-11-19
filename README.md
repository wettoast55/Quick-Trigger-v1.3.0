# Quick Trigger

A small Windows app that watches a screen area for a chosen pixel color. It can record trigger events or send a left-click when the color appears.

## Run

Extract the ZIP and open **QuickTrigger.exe**. Requires 64-bit Windows and .NET Framework 4.8. No installer or package downloads are needed.

1. Choose a monitor from **Display**. Each entry shows its device name, resolution and primary status.
2. Click **Test scene** for a quick check, or focus your own test window and press **F9** to bind it. F9 automatically selects that window's monitor.
3. Choose an area with **Select area** or **Center**. Exact coordinates are on **Advanced**.
4. Enter a six-digit hex color, use **Choose**, or hover over a pixel and press **F8**.
5. Set **Cooldown (ms)** and **Scan interval (ms)**. Both accept **1**.
6. Press **F6** to start. Press **F7** to stop immediately.

The app starts paused, with clicking off. Move or minimize Quick Trigger if it covers the scan area. Editing a setting pauses the current run.

## Green scan-area outline

A bright green outline shows the exact scan rectangle when the app first opens, when you change displays, and after selecting, centering, moving or resizing the area. **Show area** displays it again whenever you need it.

The outline stays bright for **two seconds**, fades during the **third second**, and then closes. Another area adjustment restarts the three-second countdown. Its center is open and the overlay is configured to let pointer input pass through without taking focus.

Showing the outline pauses monitoring. Starting a scan or sampling a color removes it first and waits for the desktop's pending drawing changes, so the indicator is not deliberately analyzed as a matching color. F7 also dismisses it.

## Multiple displays

**Display** determines where **Select area**, **Center** and the test scene operate. Selection is confined to that monitor, and the whole scan area must fit within it. Changing the display centers the current area there and pauses the run. A bound click target on another monitor is cleared.

F9 selects the bound window's monitor and centers the area within that window. Advanced coordinates remain absolute physical desktop coordinates, including negative values for monitors above or to the left of the primary screen. Regions that would cross a monitor edge are kept within the selected display when centered or selected; manually entered out-of-display coordinates are rejected when starting.

The monitor list refreshes when opened. If the selected monitor moves, changes resolution or disconnects, monitoring stops so you can check the area again.

## Clicking

Enable **Click on trigger**, focus the bound window, put the pointer inside it, then press **F6**. Each trigger sends a left-button down/up pair at the current pointer position. Quick Trigger does not move or aim the pointer.

Enable **Repeat while color is present** for repeated clicks after each cooldown. With repetition off, one continuous color match produces only one event. Under **Advanced**, **Stable frames** controls how many consecutive matching scans are needed; set it to 1 if you want a single matching scan to qualify.

Clicking requires the bound window to be in the foreground, the pointer inside its client area, and the left mouse button released. The app checks the window again immediately before sending input. If F7 is already used by another application, clicking stays unavailable until the conflict is resolved and Quick Trigger restarted.

## Timing

Cooldown and scan interval accept whole milliseconds from **1 to 2,147,483,647**, the positive 32-bit integer range. The previous custom lower and upper caps are gone. The defaults remain 250 ms cooldown and 33 ms scan interval.

The scanner runs on a separate thread and uses a high-resolution Windows waitable timer where supported. The preview and interface refresh about ten times a second without controlling the scan rate. Scans run sequentially; when one takes too long, the app skips the missed deadline instead of building a queue.

**1 ms is a requested interval, not a guarantee of 1,000 scans or clicks per second.** Capture cost, Windows scheduling, hardware load and the display's own refresh rate affect the result. Repeated captures can read the same displayed frame. **Actual scans/s** reports measured throughput. The timer falls back to a standard waitable timer on older systems.

See `test-results.txt` for measured timer-only waits from the included validation run. Those measurements exclude screen capture and input delivery.

## Interface and shortcuts

| Item | Purpose |
| --- | --- |
| Setup | Display, color, timing, repeat/click options and live preview |
| Show area | Show the green scan-area outline for three seconds |
| Advanced | Physical coordinates, area size, minimum pixels and stable frames |
| Events | Latest 100 trigger records |
| F6 | Start or stop |
| F7 | Stop, including during a long scheduled wait |
| F8 | Sample the pixel under the pointer and pause |
| F9 | Bind the active window, select its monitor, center the area and pause |

Custom areas remain fixed on the desktop if a window moves. Moving or resizing the built-in scene updates its area and pauses monitoring. Negative monitor coordinates are supported. Capture width and height remain limited to 1024 pixels each.

## Test scene

The colored target crosses a dotted 120 x 120 scan area. **Space** pauses movement and centers it. **T** hides/shows the target. **D** toggles a same-color square to demonstrate false positives. **R** resets the received-click counter, which includes manual clicks.

The detector counts matching pixels throughout the area. Each red, green and blue channel must be within the selected tolerance. It does not recognize characters or distinguish a target from a same-color UI element, particle or decoy.

## Logs

**Export CSV** stops monitoring and saves up to the most recent 10,000 retained events. Nothing is exported until you choose a destination. If a busy interface causes pending events to overflow, the Events page reports that separate dropped-event count.

- `would_fire`: the trigger qualified in detection-only mode.
- `click_sent`: Windows accepted the input pair. This does not prove the target application accepted it.
- `input_failed`: Windows rejected the input pair and monitoring stopped.

Exports include UTC time, matching-pixel count/fraction, capture duration, observed presence duration, region, color and timing settings. Presence duration begins at the first eligible matching scan, not at the game's first rendered frame. It is not an end-to-end reaction-time measurement.

Windowed or borderless test builds work best with desktop capture. Occlusion, exclusive fullscreen, protected content, HDR conversion and input filtering can affect results. This app uses ordinary Windows capture/input APIs and has no anti-cheat integration or bypass.

## Source and building

Run **build.cmd** to rebuild with the .NET Framework compiler. Run **test.cmd** to execute the tests and write `test-results.txt`. Tests never send clicks or capture another application's screen.

| File | Responsibility |
| --- | --- |
| `Detection.cs` | Settings, pixel matching, trigger rules and CSV records |
| `ScanWorker.cs` | Background capture and sequential trigger processing |
| `ScanTimer.cs` | Short, cancellable Windows timer waits |
| `WindowsApi.cs` | Documented Windows interop calls |
| `MainForm.cs` | Controls, hotkeys, preview updates and export |
| `Ui.cs` | Shared control styles and preview drawing |
| `DemoForm.cs` | Built-in target scene |
| `RegionPicker.cs` | Drag-to-select area overlay |
| `AreaOverlay.cs` | Bright green setup outline and three-second fade |
| `DisplayChoice.cs` | Display selection, layout checks and physical region geometry |
| `SelfTests.cs` | Detector, timing, export and input-control checks |
| `Program.cs` | Startup and executable metadata |

Comments explain each class, method and non-obvious block. Names describe their purpose. No external packages, obfuscation, network calls, game hooks or drivers are used.

Windows API references: [CreateWaitableTimerEx](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw), [SetWaitableTimer](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-setwaitabletimer), [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput), [CopyFromScreen](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.graphics.copyfromscreen).
