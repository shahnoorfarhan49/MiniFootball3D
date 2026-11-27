# MiniFootball3D

MiniFootball is a single-player, five-a-side football league for Windows. It uses Unity **6000.5.8f1**, C#, the existing URP project settings, and the Unity Input System.

## Generate and launch on Windows

1. Open this checkout with Unity **6000.5.8f1**. Wait for package import and script compilation.
2. Select **MiniFootball > Build Complete Game**.
3. Open `Assets/MiniFootballGenerated/MiniFootball.unity` and enter Play Mode. Click inside the Game view to give it keyboard focus.
4. For a standalone build, select **File > Build Profiles**, activate Windows, confirm that the generated MiniFootball scene is first in the scene list, then build and run.

The menu command saves the playable scene, connected camera and game controller, procedural stadium and ball, stylized character prefab, URP materials, and three animation clips into `Assets/MiniFootballGenerated`. The controller draws the menu and HUD in the generated scene. Generation owns that directory and refreshes its outputs in place. Keep hand-edited assets elsewhere. It preserves existing build-scene entries and adds the generated scene first. Keep the supplied SampleScene and its original URP settings for reference.

## Play

Create a club with two kit colors and an original badge. Choose six of the eight available players. The five starting slots must include a goalkeeper in slot one, a defender, a midfielder, and a forward. Select two lineup rows to swap them. The final slot is the substitute. The two unselected players are listed as free agents. The naming option lets a season use fictional club and player names. Ratings are custom game values.

Each club plays the others once. Play seven three-minute fixtures. Each completed user fixture advances its entire round and simulates the other three results. A win earns three points and a draw earns one. Standings use points, goal difference, then goals scored. After round seven, the Champion Celebration shows the league winner.

| Input | Action |
| --- | --- |
| WASD | Move the selected player |
| Shift | Sprint while stamina is available |
| J | Pass and switch to the receiving player |
| K | Shoot toward the opposing goal |
| L | Tackle a nearby opponent with possession |
| Q | Switch to the teammate closest to the ball |
| Esc | Pause or resume. Select a valid replacement in the pause menu to use the substitute. |

MiniFootball uses arcade rebounds at the pitch boundaries. Goals restart play from the center. Scores and player appearances, goals, and assists are saved at full time. Club creation and lineup changes also save immediately. Continue Game restores the club and the current round. The table is reconstructed from saved results. Leaving a match before full time discards the match. New Season can retain your club while resetting fixtures and stats, or replace it with a new club.

## Validation

The standalone regression harness uses the actual league source and lightweight Unity API stand-ins. With .NET 8, run `dotnet run --project Tests/League/LeagueChecks.csproj`. It checks the roster and draft rules, all 28 fixtures, season progression, scoring, standings tie-breakers, statistics, and save-file handling. Use a C# syntax parser and `git diff --check` for cloud checks.

### Required Windows Unity smoke tests

These checks require local Unity and are **unrun in the cloud**.

- Import the project with the pinned editor. Check the Console for package, script, and shader errors.
- Run **Build Complete Game** twice. Confirm the generated scene remains a single build entry, existing scenes are retained, and generated prefabs and animation references remain connected. Check that an open modified generated scene is protected.
- Create a club from the new-game screen. Try a duplicate pick, a lineup without its keeper or a required outfield position, and a valid lineup. Verify the badge, kit colors, naming mode, two free agents, lineup editing, and reload through Continue Game.
- Start a match at 16:9 and in a resized window. Verify the clock, ball physics, team differentiation, selection marker, visible animations, shadows, goalposts and nets, camera framing, movement, sprint and stamina, pass, shot, tackle, switching, keeper behavior, valid substitutions, pause and resume, and kickoff after a goal. Check sideline rebounds and shots that strike or miss the posts.
- Complete a match. Verify the score in both home and away fixtures, the three simulated AI results, points, player statistics, and save reload. Verify that leaving an incomplete match preserves the previous saved round. Finish all seven rounds and verify the champion and New Season flows.
- Make a Windows build from the generated scene. Launch outside the Editor and repeat the first-run, input, and persistence checks.
