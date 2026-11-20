# Pop Quiz

**SWE Faculty Edition · April Fools**

Welcome to the world's most overqualified attendance assistant. It watches a chosen screen area for a color, takes attendance in the event log, and can raise a hand by sending a left-click. The school names are the joke; the controls still work.

## First day of class

1. **Unzip the backpack.** Extract the whole ZIP and open **PopQuiz.exe**. Keep `PopQuiz.exe.config` beside it. Bring 64-bit Windows and .NET Framework 4.8; a permission slip is unnecessary.
2. **Find your classroom.** On **Homeroom**, choose your monitor under **Classroom display**. Open **Practice lab** for the included moving-target scene. For your own test window, focus it and press **F9** to assign it as the classroom.
3. **Claim some chalkboard.** Use **Mark board** to drag a scan rectangle, **Front row** to center it, or **Outline** to show its green border. The border stays bright for two seconds, then fades out during the third. Exact coordinates live under **Extra Credit**.
4. **Choose your school colors.** Enter a six-digit **House color (hex)**, use **Pick crayon**, or point at a pixel and press **F8** to borrow its color. **Grading curve** controls how much each RGB channel may differ.
5. **Set the bell schedule.** **Recess (ms)** is the cooldown between submitted triggers. **Roll call (ms)** is the scan interval. Both accept 1 ms, but Windows occasionally arrives late to class.
6. **Give the student a moment to think.** Enable **Thinking time (random 1-50 ms)** for a fresh whole-millisecond delay before each trigger. Both 1 and 50 are included. **Extra homework (repeat)** allows another trigger while the color remains present, after the cooldown and a new delay.
7. **Issue a hall pass, if needed.** Enable **Hall pass (hold)** and choose the **Hall-pass button**. Release all buttons, hold your chosen key or mouse button, then release it to finish. After **Take roll / F6**, scanning happens only while that button is held. Releasing it cancels any pending delayed trigger.
8. **Ring the bell.** Click **Begin class / F6**, or press **F6**, to start. Press F6 again or click **Dismiss / F6** to stop. **F7 always means stop / recess**, including during a delay. Editing a setting also pauses the run.
9. **Read the report card.** Open **Report Card** to inspect the latest 100 events. **Think (ms)** is the sampled delay; **Actual (ms)** is the measured wait. Use **Save grades** to export up to the latest 10,000 records as CSV. There is no grade inflation: requested and actual times are recorded separately.

The app starts paused, with automatic clicking and thinking time off. Move or minimize Pop Quiz if it covers the chalkboard area.

## Optional participation credit

**Raise hand (send clicks)** enables actual left-clicks. Focus the assigned test window, put the pointer inside its client area, release the physical left mouse button, then start with F6. Each qualifying event sends one down/up pair at the current pointer position. The pointer is never moved or aimed.

Use another key or mouse button for a hall pass when clicks are enabled. F7 must be available, and the assigned window must still be in the foreground. Focus, pointer location and click eligibility are checked again after the delay.

## Faculty translation sheet

| School label | Actual control |
| --- | --- |
| Homeroom | Main setup page |
| Extra Credit | Coordinates and detection filters |
| Report Card | Event history |
| Classroom display | Selected monitor |
| Chalkboard area | Screen region to scan |
| Mark board / Front row / Outline | Select / center / show the scan area |
| House color / Crayon box | Target hex color / color picker |
| Grading curve | RGB channel tolerance |
| Recess (ms) | Trigger cooldown |
| Roll call (ms) | Scan interval |
| Thinking time (random 1-50 ms) | Random delay before each trigger |
| Extra homework (repeat) | Repeat while the color remains present |
| Raise hand (send clicks) | Send a left-click on a qualifying trigger |
| Hall pass (hold) / Hall-pass button | Require a held activation key or button |
| Passing pixels | Minimum number of matching pixels |
| Proofreads (frames) | Required consecutive matching scans |
| Desk X / Desk Y / Board width / Board height | Scan-region coordinates and size |
| Practice lab | Built-in test scene |
| Save grades | Export CSV |
| Begin class / Take roll / Dismiss | Start / arm hold mode / stop |

## Practice lab: no homework required

The student crosses the dotted chalkboard. **Space** freezes movement and centers the student. **T** hides or shows them. **D** introduces the class clown: a same-color decoy demonstrating that matching pixels do not prove target identity. **R** clears the received-click counter, which also includes manual clicks.

For a quick timing demo, enable **Thinking time** and **Extra homework**, begin class, and inspect **Report Card**. Turn Thinking time off and restart to compare: both delay columns then read zero.

## Fine print from the principal

- A requested 1-50 ms delay can take longer because of Windows scheduling. It is not a guarantee of end-to-end reaction time.
- Cooldown begins when a trigger is submitted. A cancelled pending trigger creates no event and consumes no cooldown.
- No additional frames are captured during the delay; the event uses the qualifying frame. This is color matching, not object recognition.
- The CSV keeps its technical column names so existing analysis scripts still work. Files default to `PopQuiz-report-card-<timestamp>.csv`.
- This edition changes names and instructions. The detector, random delays, hold controls and input checks retain the same behavior as version 2.3.

## For the teaching assistants

Run **build.cmd** to rebuild **PopQuiz.exe** with the installed .NET Framework compiler. Run **test.cmd** for the automated checks. Tests use synthetic frames and never send clicks or capture another application's screen.

`TECHNICAL-NOTES.md` contains the full operating reference, `VALIDATION.md` records what was checked, and `src` contains the readable, commented source. Version **2.3.1**. Class dismissed.

