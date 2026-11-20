# Pop Quiz technical notes

A small Windows app that watches a screen area for a chosen pixel color. It can record trigger events or send a left-click when the color appears.

## Run

Extract the ZIP and open **PopQuiz.exe**. Requires 64-bit Windows and .NET Framework 4.8. No installer or package downloads are needed.

1. Choose a monitor from **Classroom display**. Each entry shows its device name, resolution and primary status.
2. Click **Practice lab** for a quick check, or focus your own test window and press **F9** to bind it. F9 automatically selects that window's monitor.
3. Choose an area with **Mark board** or **Front row**. Exact coordinates are on **Extra Credit**.
4. Enter a six-digit hex color, use **Pick crayon**, or hover over a pixel and press **F8**.
5. Set **Recess (ms)** and **Roll call (ms)**. Both accept **1**. Enable **Thinking time (random 1-50 ms)** to add a fresh delay before each trigger.
6. For button-controlled operation, enable **Hall pass (hold)** and click the **Hall-pass button** control to choose a key or mouse button.
7. Press **F6** to start or arm. Press **F7** to stop immediately.

The app starts paused, with clicking off. Move or minimize Pop Quiz if it covers the scan area. Editing a setting pauses the current run.

## Hall pass (hold)

Enable **Hall pass (hold)** on Homeroom. The default binding is **Mouse 4 (Back)**. Click the button below **Hall-pass button** to change it: release all buttons, hold your chosen key or mouse button, then release it to finish. Esc cancels without changing the old binding.

Press **Take roll / F6** once. The status shows **Hall pass needed** until you hold the chosen button. While held, the scanner looks for your configured color and can trigger according to the usual settings. Releasing the button pauses capture, clears the preview and matching-pixel count, and discards any partial matching streak. Another hold starts fresh; the cooldown between triggers is still preserved. F6 disarms and F7 stops, even while the button remains held. Rebinding or changing settings also disarms the app.

Turn **Hall pass (hold)** off to restore continuous scanning after F6. Hold mode is a press-and-hold action, not a press-once latch. The activation button is not swallowed; the foreground application can still receive its normal action.

Keyboard keys and the five standard mouse buttons are supported, including both side buttons and separate left/right modifiers. **Esc and F6-F9 are reserved.** The left mouse button is available for detection-only mode; when **Raise hand (send clicks)** is enabled, choose another hold button because generated clicks use the left button. Mouse-wheel scrolling has no held state. Extra vendor-specific buttons need to be mapped to a normal keyboard key or supported mouse button using the device's software. Bind one button, not a combination.

Between scans, hold state is checked every **5 requested milliseconds**, even with a long scan interval. Shorter scan intervals remain available. Capture in progress cannot be interrupted, so the app rechecks the hold after capture and immediately before a trigger; a released hold discards that frame. Actual response time depends on Windows scheduling and capture time. The interface updates about ten times a second.

## Green scan-area outline

A bright green outline shows the exact scan rectangle when the app first opens, when you change displays, and after selecting, centering, moving or resizing the area. **Outline** displays it again whenever you need it.

The outline stays bright for **two seconds**, fades during the **third second**, and then closes. Another area adjustment restarts the three-second countdown. Its center is open and the overlay is configured to let pointer input pass through without taking focus.

Showing the outline pauses monitoring. Starting a scan or sampling a color removes it first and waits for the desktop's pending drawing changes, so the indicator is not deliberately analyzed as a matching color. F7 also dismisses it.

## Multiple displays

**Classroom display** determines where **Mark board**, **Front row** and the test scene operate. Selection is confined to that monitor, and the whole scan area must fit within it. Changing the display centers the current area there and pauses the run. A bound click target on another monitor is cleared.

F9 selects the bound window's monitor and centers the area within that window. Coordinates on Extra Credit remain absolute physical desktop coordinates, including negative values for monitors above or to the left of the primary screen. Regions that would cross a monitor edge are kept within the selected display when centered or selected; manually entered out-of-display coordinates are rejected when starting.

The monitor list refreshes when opened. If the selected monitor moves, changes resolution or disconnects, monitoring stops so you can check the area again.

## Clicking

Enable **Raise hand (send clicks)**, focus the bound window, put the pointer inside it, then press **F6**. Each trigger sends a left-button down/up pair at the current pointer position. Pop Quiz does not move or aim the pointer.

Enable **Extra homework (repeat)** for repeated clicks after each cooldown. With repetition off, one continuous color match produces only one event. Under **Extra Credit**, **Proofreads (frames)** controls how many consecutive matching scans are needed; set it to 1 if you want a single matching scan to qualify.

Clicking requires the bound window to be in the foreground, the pointer inside its client area, and the left mouse button released. The app checks the window again immediately before sending input. If F7 is already used by another application, clicking stays unavailable until the conflict is resolved and Pop Quiz restarted.

## Timing

### Random trigger delay

Enable **Thinking time (random 1-50 ms)** on **Homeroom**. It starts off. Each qualifying trigger draws a new, uniformly chosen whole number from **1 through 50 ms, inclusive**, then waits before recording a detection-only event or sending a click. Repeat mode draws a fresh value for every event. Consecutive events can happen to draw the same value.

The delay starts after the matching-frame and cooldown requirements are met. Cooldown starts when the event is actually submitted, so the delay does not consume the gap required before the next event. The scan interval still controls capture scheduling. There is only one pending trigger at a time; no additional frames are captured during its delay. The event uses the qualifying frame rather than checking for a new color match after waiting.

F7, Stop, changing settings, or releasing the hold button cancels a pending trigger. During the delay, F7 and hold state are checked at intervals of at most **5 requested milliseconds**; the stop signal interrupts a timer wait immediately. Focus, pointer position and click eligibility are checked again before sending input. A cancelled trigger creates no event and does not consume cooldown.

On **Report Card**, **Think (ms)** is the chosen value and **Actual (ms)** is the measured time spent in the delay. Both are zero when this option is off. Windows scheduling can make the actual wait longer than requested, including longer than 50 ms. These values exclude capture and input-delivery time and are not a measurement of a game's end-to-end reaction time.

### Scan interval and cooldown

Cooldown and scan interval accept whole milliseconds from **1 to 2,147,483,647**, the positive 32-bit integer range. The previous custom lower and upper caps are gone. The defaults remain 250 ms cooldown and 33 ms scan interval.

The scanner runs on a separate thread and uses a high-resolution Windows waitable timer where supported. The preview and interface refresh about ten times a second without controlling the scan rate. Scans run sequentially; when one takes too long, the app skips the missed deadline instead of building a queue.

**1 ms is a requested interval, not a guarantee of 1,000 scans or clicks per second.** Capture cost, Windows scheduling, hardware load and the display's own refresh rate affect the result. Repeated captures can read the same displayed frame. **Actual scans/s** reports measured throughput. The timer falls back to a standard waitable timer on older systems.

See `test-results.txt` for measured timer-only waits from the included validation run. Those measurements exclude screen capture and input delivery.

## Interface and shortcuts

| Item | Purpose |
| --- | --- |
| Homeroom | Display, color, timing, hold/repeat/click options and live preview |
| Hall pass (hold) | Enable the selected key or mouse button as a hold requirement |
| Hall-pass button | Choose the activation binding; changes pause the run |
| Thinking time (random 1-50 ms) | Add a newly sampled delay before each qualifying trigger |
| Outline | Show the green scan-area outline for three seconds |
| Extra Credit | Physical coordinates, area size, minimum pixels and stable frames |
| Report Card | Latest 100 trigger records |
| F6 | Start/stop continuous mode, or arm/disarm hold mode |
| F7 | Stop, including during a long scheduled wait |
| F8 | Sample the pixel under the pointer and pause |
| F9 | Bind the active window, select its monitor, center the area and pause |

Custom areas remain fixed on the desktop if a window moves. Moving or resizing the built-in scene updates its area and pauses monitoring. Negative monitor coordinates are supported. Capture width and height remain limited to 1024 pixels each.

## Practice lab

The colored target crosses a dotted 120 x 120 scan area. **Space** pauses movement and centers it. **T** hides/shows the target. **D** toggles a same-color square to demonstrate false positives. **R** resets the received-click counter, which includes manual clicks.

The detector counts matching pixels throughout the area. Each red, green and blue channel must be within the selected tolerance. It does not recognize characters or distinguish a target from a same-color UI element, particle or decoy.

## Logs

**Save grades** stops monitoring and saves up to the most recent 10,000 retained events. Nothing is exported until you choose a destination. If a busy interface causes pending events to overflow, the Report Card page reports that separate dropped-event count.

- `would_fire`: the trigger qualified in detection-only mode.
- `click_sent`: Windows accepted the input pair. This does not prove the target application accepted it.
- `input_failed`: Windows rejected the input pair and monitoring stopped.

Exports include UTC time, matching-pixel count/fraction, capture duration, observed presence duration, region, color, timing settings, hold-mode state and the binding's Windows virtual-key code. Presence duration begins at the first eligible matching scan, not at the game's first rendered frame. It is not an end-to-end reaction-time measurement.

Version 2.3 appends three CSV columns: `random_delay`, `requested_delay_ms` and `actual_delay_ms`. The first records whether the option was enabled, the second records the chosen whole-millisecond delay, and the third records the measured wait with millisecond decimals. Existing columns keep their positions. `eligible_presence_ms` ends at the qualifying scan and excludes the added delay. Cancelled pending triggers are not exported.

Windowed or borderless test builds work best with desktop capture. Occlusion, exclusive fullscreen, protected content, HDR conversion and input filtering can affect results. This app uses ordinary Windows capture/input APIs and has no anti-cheat integration or bypass.

## Source and building

Run **build.cmd** to rebuild with the .NET Framework compiler. Run **test.cmd** to execute the tests and write `test-results.txt`. Tests never send clicks or capture another application's screen.

| File | Responsibility |
| --- | --- |
| `Detection.cs` | Settings, pixel matching, trigger rules and CSV records |
| `ScanWorker.cs` | Background capture and sequential trigger processing |
| `Activation.cs` | Binding names/validation, hold gating and scan deadlines |
| `BindingForm.cs` | Choose one held keyboard key or mouse button |
| `ScanTimer.cs` | Short, cancellable Windows timer waits |
| `TriggerDelay.cs` | Inclusive 1-50 ms sampling and cancellable trigger-delay waits |
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

Windows API references: [CreateWaitableTimerEx](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw), [SetWaitableTimer](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-setwaitabletimer), [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput), [CopyFromScreen](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.graphics.copyfromscreen), [GetAsyncKeyState](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getasynckeystate), [Virtual-key codes](https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes).
