# Hop On!

A Bus Jam-style puzzle made for the Rollic Game Developer case: tap the passengers out of the crowd and onto the buses of their colour before the time runs out.

Unity **2022.3.19f1**, URP, portrait, iOS and Android.

## Running it

- Open the project with Unity 2022.3.19f1. There is no Git LFS: a plain clone or ZIP works.
- **Game:** `Assets/_Project/Scenes/Game.unity`. It is the only scene in the build. Press Play.
- **Level editor:** `Assets/_Project/Scenes/LevelEditor.unity`. Press Play.
- **Tests:** open Window > General > Test Runner > EditMode. There are 56 tests.
- Progress is saved between runs. In the Unity Editor, **Hop On > Delete Save** starts over from level 1.

## The brief, point by point

| Brief | Where it is |
|---|---|
| Start, play and end screens | `GameFlow`, a state machine (Start, Playing, Ending, Won or Lost) that drives `StartView`, `HudView`, `WinView` and `LoseView` |
| Timer as a constraint, tap to start | The start screen shows the level's time. The countdown starts when the Level button is tapped |
| Timer as a fail condition | `LevelSession.Tick` loses the level with `TimeUp` at zero |
| Level indicator | "Level N" in the HUD and on the start screen. The number follows the catalog and loops back to 1 after the last level |
| Resume where the player left off | `SaveStore` and a replay of the saved moves (see below) |
| 5+ unique levels | `Data/Levels/Level_01` to `Level_05`. A test checks that each one can be finished |
| Level editor: add levels, update levels, edit the timer | `Scenes/LevelEditor.unity` (see below) |

## Architecture

The dependencies go one way: Flow and Tools use World, UI and Data, and those use Logic. Logic uses nothing.

```
Assets/_Project/Scripts
  Logic/   HopOn.Logic assembly, pure C# with no UnityEngine: every rule, unit tested
           Level, LevelValidator, Board (BFS path to the top row), BusQueue, WaitingArea, LevelSession, LevelSolver
  World/   the 3D picture: LevelView, PassengerView, BusView, BoardLayout, BoardCamera, ColorGrade
  UI/      the screens (UIScreen base, Start, HUD, Win, Lose), Haptics
  Flow/    GameFlow: the screen flow, the timer, saving
  Data/    LevelCatalog, CatalogLevelProvider (ILevelProvider), SaveStore, SavedGame
  Tools/   LevelEditor
  Tests/   EditMode tests
```

**The logic decides, the view catches up.**
- A tap is settled completely inside `LevelSession.Tap`. The passenger's seat is reserved at once, on the bus or in a waiting seat, and C# events report each step.
- The view plays those events back at its own pace: DOTween moves the passengers and buses, an Animator moves arms and legs.
- So two fast taps can never take the same seat, and a level can never be both won and lost.
- The view reports back only what the screen alone knows:
  - `BusArrived`: the next bus has pulled in;
  - `BusBoarded`: the riders of a full bus have crossed the line in front of the waiting seats;
  - `WaitingSeatsFilled`: every waiting passenger has crossed that line.

## The rules

These are the mechanic decisions. The stop rules were checked against Bus Jam on a phone, case by case.

- **Tapping.** A passenger leaves only if a path through empty floor reaches the top row; otherwise it shakes. If the bus at the stop is its colour and has room, it walks to the bus. Otherwise it takes the first free waiting seat: there are 5, and nobody slides over.
- **Buses.** A bus takes 3 riders and leaves when full. The stop stays empty until the next bus has pulled in on screen, and only then does that bus take the waiting passengers of its colour. A tap during the change goes to a waiting seat, never to a bus that is not there yet.
- **The race for the last seat.** A tap can take the last waiting seat while a bus is still at the stop. The level is then lost, unless the riders that fill that bus get there first. Both sides are judged at one line in front of the waiting seats, once a frame; if they cross in the same frame, the riders win.
- **The rider tapped first is protected.** While the riders who filled a bus are on their way, a tap that would take the last free seat does nothing until they are in. A player is never punished for tapping the right passenger first.
- **Nowhere to go.**
  - During a race, a passenger with no seat sets off and waits just off the board, at most 3 at a time. It takes the first seat a bus frees, or boards that bus if it is its colour.
  - While buses change with every seat taken, such a tap does nothing.
- **Losing** is checked at two moments only:
  - a tap took the last seat beside a bus, and that passenger got there before the bus was filled;
  - a bus pulled in and took none of the waiting passengers, with every seat taken.

  A seat filled by the waiting line moving up decides nothing, as in Bus Jam.
- **The end of a level.**
  - Anyone still walking stops and stands where they are.
  - A loss keeps the board on screen for 1.5 s, so the player sees why, then shows "No Space Left!" (a cross) or "Time's Up!" (a clock).
  - A win plays fireworks before the win card.
- **Timer.** Each level has its own time limit. It runs only while playing; at zero the level is lost, after a rider in mid-jump has landed.
- **Feel.**
  - A light haptic when a passenger sets off: a native plugin on iOS, a tick vibration on Android. A blocked or held tap gives none.
  - Every finger counts on a phone, so fast taps with two thumbs all go through.
  - Portrait, 60 fps.

## Levels

A level is a JSON file in `Assets/_Project/Data/Levels`:

```json
{
    "timeLimitSeconds": 60,
    "buses": "RBG",
    "cells": [".R.B.", "GRBG.", ".RBG.", "....."]
}
```

- `cells` lists the rows, from the top row (the exit side) down.
  - `.` is floor, a space is a hole, `#` is a block.
  - `R B G Y P O` are passengers of six colours.
- `buses` gives the bus colours in arrival order, one letter per bus.
- The `LevelCatalog` asset lists the files in play order, which is also their number in the game.
- The game reads levels through `ILevelProvider`, so a remote provider for live levels would plug in without other changes.
- Every level is checked when it loads. A broken level is reported with its problems instead of being played, and the tests run the same checks on every shipped level:
  - the time is above zero and there is at least one bus;
  - each colour has exactly as many passengers as its buses have seats;
  - the top row has an exit;
  - no passenger is walled in for good.
- Boards go up to 7x7, so that every level fits the screen.

## Level editor

`Scenes/LevelEditor.unity`, with the game's own board view and camera, so a level looks exactly as it will in the game.

- **Levels.** Open any level from the catalog or start a new one. **Save** overwrites the level's file; **Save as new** adds a new file and appends it to the catalog.
- **Board.** Brushes for floor, hole, block and the six colours: click or drag to paint. Resize from 3 to 7 cells each way; every cell keeps its row and column.
- **Time.** Change the level's time limit in 5-second steps.
- **Buses.**
  - Add a bus by colour, or select one to move it, remove it, or insert after it.
  - **Auto** makes one bus per 3 passengers of a colour, in the order the colours first appear from the top.
- **Undo and redo** for every edit, with buttons or Cmd/Ctrl + Z.
- **Live checks.** The level is checked on every change and the problems are listed. A level that breaks a rule cannot be saved or tested.
- **Finish check.**
  - `LevelSolver` plays copies of the real `LevelSession`, so there is no second copy of the rules. It goes through the moves depth first and remembers the positions it has seen.
  - It reports **can be finished**, **cannot be finished** or **too big to check**.
  - It runs off the main thread, never during a drag, within a budget of 20,000 positions.
  - It counts taking the last seat beside a bus as lost, so a level that only a perfectly timed race could finish counts as not finishable.
  - Saving such a level gives a warning.
- **Test** plays the level right there with the real view. **Play solution** plays the solution the check found.
- Loading another level with unsaved edits asks first.

## Saving and resuming

- `save.json` in the app's persistent data folder holds the level's id and number. For a level left in the middle, it also holds the moves (taps and bus arrivals) and the time left.
- **When it is written:**
  - after every move;
  - when the app is paused or quits;
  - when a level is decided: the next level after a win, the same level from the start after a loss.
- **How it is written:** to a temporary file first, which then replaces the old save in one step, so a crash never leaves half a save. A broken save starts the game fresh.
- **How it resumes:** the saved moves are replayed through `LevelSession`. There is no separate copy of the board that could disagree with the rules.
  - The resumed level shows the bus at the stop, and a race that was running is decided in the player's favour.
- Progress is stored by level id, so it follows its level when the catalog changes.

## Performance

The game was tuned with measurements on a low-end Android phone (Realme C55).

- **No post-processing pass.** The colour grade (post exposure, colour adjustments, vignette) is stored as a LUT and applied inside the world's own shaders (`Art/Grading`). Phones never pay for a full-screen pass.
- **Flat planar shadows** under passengers and buses instead of shadow maps.
- **No HDR, a short shadow distance.**
- **Compressed textures:** about 14 MB of texture memory on iOS.

## Tests

56 EditMode tests, all passing:
- **`LevelSessionTests` (37):** every rule above, the Bus Jam stop cases included, and resuming.
- **`LevelTests` (6):** reading and checking level files. The shipped levels load, fit the screen and have unique ids.
- **`SaveStoreTests` (6):** the save file.
- **`LevelSolverTests` (7):** solvable, unsolvable and too-big levels, and that every shipped level can be finished.

## Third-party

- [DOTween](http://dotween.demigiant.com) by Demigiant, free version.
- [Fredoka](https://fonts.google.com/specimen/Fredoka), SIL Open Font License; the license file is next to the font.
- TextMesh Pro and URP, Unity packages.

## Next steps

- Bus Jam's special passengers, for example hidden "?" passengers whose colour shows once a neighbour leaves. The level format and the editor would get a code and a brush for them.
- A remote `ILevelProvider` for live levels, and more levels.
