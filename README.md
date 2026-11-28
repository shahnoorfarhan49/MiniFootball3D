# MiniFootball3D

MiniFootball is a single-player, five-a-side football league for Windows. It uses Unity **6000.5.8f1**, C#, the existing URP project settings, and the Unity Input System.

## Generate or upgrade on Windows

1. Open this checkout with Unity **6000.5.8f1**. Wait for package import and script compilation.
2. Save any open scene edits. Select **MiniFootball > Build Complete Game** to generate the v2 assets, including when upgrading an existing v1 checkout. Run **MiniFootball > Validate Generated Game** and check the Console.
3. Open `Assets/MiniFootballGenerated/MiniFootball.unity` and enter Play Mode. Click inside the Game view to give it keyboard focus.
4. For a standalone build, select **File > Build Profiles**, activate Windows, confirm that the generated MiniFootball scene is first in the scene list, then build and run.

The generator writes the match scene, character and stadium prefabs, materials, animation clips and Animator controller into `Assets/MiniFootballGenerated`. It uses the rigged FBX included in the checkout. Blender is optional for editing the mesh source. To rebuild the FBX, run `blender -b --factory-startup --python Tools/Art/build_athlete.py` from the project root. Keep hand-edited assets outside the generated directory.

The included model has a 21-bone rig and two body detail levels. [Player model preview from Blender](Docs/Footballer.jpg)

Select **Continue Game** to resume a v1 season. The league uses the same local save file for the club, fixtures and player statistics.

## Play

Create a club with two kit colors and an original badge. Choose six of the eight available players. The five starting slots must include a goalkeeper in slot one, a defender, a midfielder, and a forward. Select two lineup rows to swap them. The final slot is the substitute. The two unselected players are listed as free agents. The naming option lets a season use fictional club and player names. Ratings are custom game values.

Each club plays the others once. Play seven three-minute fixtures. Each completed user fixture advances its entire round and simulates the other three results. A win earns three points and a draw earns one. Standings use points, goal difference, then goals scored. After round seven, the Champion Celebration shows the league winner.

| Input | Action |
| --- | --- |
| WASD | Move relative to the broadcast camera. Hold a direction to aim a pass or pick a side of the goal. |
| Shift | Sprint while stamina is available |
| J | Pass to a teammate in the aimed direction or take a throw-in |
| K | Tap for a quick shot or hold and release for more power. Near a high, loose ball, press to jump for a header. |
| L | Attempt a tackle within reach of the opposing carrier |
| Q | Switch to another teammate near the ball’s projected path |
| Esc | Pause or resume. Use the pause menu for a substitution or to change match settings. |

When the whole ball crosses a touchline, the opponents of the last team to touch it take a throw-in. A ball crossing the goal line outside the goal results in a goal kick after an attacking touch or a corner after a defending touch. When the taker is ready, aim and press J for a pass or throw. Use K for a shot or clearance from a kicking restart. The taker passes automatically after three seconds without input. The home club kicks off first, and the conceding side kicks off after a goal. **Match Settings** in the main or pause menu controls the broadcast camera height, goal replays, graphics preset and frame-rate cap.

Scores and player appearances, goals, and assists are saved at full time. Club creation and lineup changes also save immediately. Continue Game restores the club and the current round. The table is reconstructed from saved results. Leaving a match before full time discards the match. New Season can retain your club while resetting fixtures and stats, or replace it with a new club.

## Validation

With .NET 8, run `dotnet run --project Tests/League/LeagueChecks.csproj` to exercise the league and football rules outside the Editor. Run the Windows acceptance pass on the target laptop to verify the humanoid import, animation contact, live physics, tactical behavior, replays, season compatibility and frame times. [Project audit and Windows test checklist](Docs/Version2Validation.md)
