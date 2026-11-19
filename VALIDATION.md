# Validation record

Build date: November 18, 2026.

- Compiled successfully with the installed 64-bit .NET Framework compiler, with no warnings after the final source update.
- All 34 deterministic tests passed. The executable returned exit code 0. Details are in `test-results.txt`.
- The desktop executable launched and exposed its controls through Windows accessibility. The initial state was paused with automatic clicking disabled.
- Interactive validation was blocked because the window was minimized and desktop automation could not restore/control it. Actual desktop capture, region dragging, color sampling, input delivery, scene interactions and CSV export through the UI have **not been verified end to end**.
- No test was performed against Vanguard or any other anti-cheat. This build provides no evidence of bypass or invisibility.

Suggested first-run check: open the built-in scene, press F6, confirm `would_fire` events as the target crosses the region, and press F7. Next hide the target with T and enable the same-color decoy with D to observe a false positive. Test optional clicks only after the detection-only run works; reset the scene's click counter with R before measuring it.

When comparing this tester with game telemetry, record input receipt separately from shots accepted by your game's logic. A successful Windows input call and a lack of an immediate anti-cheat message are not evidence of an accepted shot or an undetected tool.
