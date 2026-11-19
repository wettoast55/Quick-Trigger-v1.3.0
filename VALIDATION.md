# Quick Trigger validation

Version 2.1.0, checked November 19, 2026.

- The 64-bit executable compiled successfully, with warnings treated as errors.
- All **82 automated checks passed**; the process returned exit code 0.
- The real form's cooldown and scan-interval controls both accepted 1 ms and expose the full positive 32-bit integer range.
- Tests cover color boundaries, bitmap counting, 1 ms cooldown boundaries, consecutive matches, repetition, cancellation of the maximum timer wait, locale-independent CSV, native input layout and an unbound click target.
- Display tests cover negative origins, vertically offset monitors, centering, edge clipping, smaller screens, missing displays and rejection of regions outside the selected monitor.
- The real form's display selector listed the attached displays. Changing its selection updated the numeric scan coordinates correctly. Forms were constructed without being shown.
- Overlay tests check bright green color, an open-center border, one-pixel areas, physical coordinate mode and opacity at 0, 2, 2.5 and 3 seconds. Opacity reaches zero at three seconds.
- Forty timer-only waits requested at 1 ms measured a median of 1.277 ms and a 95th percentile of 2.263 ms on this system. High-resolution timer creation succeeded. This excludes capture and input delivery.
- Live desktop appearance, on-screen fade completion, pointer pass-through, monitor hot-plug behavior, capture, input delivery and CSV save dialogs have not been verified end to end. Automated tests do not show an overlay, send clicks or capture another application's screen.
- No anti-cheat compatibility or bypass testing was performed.

For a live check, choose each display, press **Show area**, and confirm the green rectangle fades away after three seconds. Select or edit an area and check that the rectangle moves accordingly. Then use the built-in test scene in detection-only mode before testing optional clicks.
