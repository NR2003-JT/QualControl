<p align="center"><img src="src/logo.png" alt="QualControl" width="204"></p>

# QualControl

Custom qualifying for **NASCAR Racing 2003 Season**. QualControl runs qualifying formats the game doesn't have: single
car runs, knockout rounds and the Indy formats, with a TV timing box and true DNQs. It works with stock NR2003 or
alongside any caution mod, because it doesn't touch cautions at all.

On top of that it can set the running order from practice lap times, save qualifying results and reapply them in a later
weekend, and merge several qualifying sessions (two groups with two different rosters, for example) into one field,
with a race roster written for you.

Download the latest release from the [Releases](../../releases) page. Each option in the app has a tooltip that
explains it.

## What it does

- **Qualifying formats.** Run in the practice session before qualifying, then written to the real grid in the
  qualifying session:
  - *Single car runs:* one car at a time with its own out lap and timed laps, released on a gap you choose.
  - *Knockout rounds:* everyone out at once, the fastest go through to the next round.
  - *Indy oval:* single car runs ranked by their lap average (with mph).
  - *Indy road / street:* Group 1, Group 2, Fast 12, Fast Six.
- **True DNQs.** Set how many cars make the race; the rest are marked on the results, held in their boxes in Happy Hour
  and can be taken out before the green.
- **Your own run.** You can drive your turn like an AI car: a banner tells you when to go, your timed laps and when to
  come in, and the game can pause at your turn.
- **Running order from practice.** Time Practice 1 and set the qualifying order from the best laps, fastest first or
  fastest last; cars without a lap go to the other end. Or draw the order now and drag drivers where you want them.
  Set it in the same session with a click, or keep the order for a new session.
- **Save and reapply results.** Save the qualifying results to a file. Reapplying writes those times exactly in a later
  weekend's qualifying session, so you can change the weather or time of day and keep the grid.
- **Merge sessions.** Merge the saved results of several qualifying sessions into one order: by the round reached
  (knockout / Indy road), then the time. A driver in more than one session keeps their best; ties go to the earlier
  session. Drivers are told apart by their .car file, so two rosters can share car numbers.
- **Race field and roster.** Choose how many cars make the race (up to 43, or 46 with a patched exe) and whether your
  own car always makes it or can fail to qualify. QualControl writes a new roster `.lst` for the race weekend: a copy
  of your roster with only the `+` / `-` marks changed.
- **TV timing box.** Pole on top, the car on its run with a live gap to pole, then its lap and place. A classic and a
  broadcast style (with telemetry), placed and sized where you want them over the game.

## Requirements

- Windows 10/11 and NASCAR Racing 2003 Season.
- QualControl runs as administrator, because NR2003 normally does and QualControl reads and writes its memory.
- The timing box and the banner need NR2003 running windowed or borderless.

## Building from source

C# WinForms, .NET Framework 4, x86. You need the C# compiler that ships with Windows:

    build.cmd

This writes `bin\QualControl.exe` and the offline qualifying simulator `bin\QualSim.exe`. If you add a source file,
add it to `build.cmd`.

## Tests

`QualSim` drives the qualifying code against a fake game with no NR2003 running. Run it after a logic change:

    bin\QualSim.exe

The full run takes a while. `QualSim practice`, `QualSim saved`, `QualSim merge` and `QualSim field` run just the
running order, saved results, merge and race field / roster checks. The simulator uses temporary files, never your
real ones. `test\RenderTabs.cs` and `test\RenderMerge.cs` draw the windows off screen for layout checks.

## Layout

- `src\` the app: `GameMemory.cs` and `Addr.cs` (game memory), `Qualifying.cs` (the qualifying formats, the grid, saved
  results), `Merge.cs` and `MergeForm.cs` (merging, the race field and the roster), `QualOverlay.cs` and
  `PlayerBanner.cs` (the timing box and banner), `MainForm.cs` (the window).
- `test\` the simulator and the render tools.
- Saved results go to `%LOCALAPPDATA%\NR2003QualControl\Saved results\` as plain text files.

## How it was built

QualControl grew out of the qualifying side of [RaceControl](https://github.com/NR2003-JT/RaceControl) and was written
with AI assistance (Claude), working from live NR2003 sessions. Memory addresses were found by probing the running game
and are documented where they are defined.

## Credits

- **Jonathon Thomas**: QualControl.

## License

MIT, see [LICENSE](LICENSE).
