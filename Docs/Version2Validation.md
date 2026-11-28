# MiniFootball 2.0 audit and Windows acceptance checks

## Findings in the playable first version

| Area | What the original project did | Effect during a match |
| --- | --- | --- |
| Character silhouette | `FootballArt.Character` put separate spheres, cylinders and boxes under four limb pivots. Every body shared the same outline. | Shoulders, elbows, hips and knees had rigid seams. Player identity depended mostly on colors. |
| Animation | `FootballArt.Motion` produced three legacy transform clips. Run had one cycle length and sprint multiplied playback by 1.33. `Match.Strike` launched the ball on the same update that started the kick. | Travel speed and stride could disagree. The ball could leave the foot during the wind-up. |
| Movement and possession | `Match.Move` immediately set the current movement speed and wrote the transform. `TrackBall` steered the rigidbody toward one point ahead of the carrier each physics tick. | Starting, stopping and turning felt abrupt. Dribbling looked tethered and a nearby receiver could control a fast ball instantly. |
| Rules and team play | `KeepBallInPlay` reflected velocity at boundaries. Every physics step chose the closest outfielder to chase while other outfielders followed fixed slots shifted by ball position. The keeper mostly followed the ball laterally. | Wide shots and clearances bounced back into play. Passing lanes, marking and changes of possession had little effect on team shape. |
| Camera and venue | `Match.UpdateCamera` followed a blend of the player and ball from a fixed offset and field of view. Pitch stripes, stand rows, line segments and goal net strands were separate primitive renderers. | Framing changed little between a long pass and close control. The stands were bare and geometry was split across many renderers. |
| Rendering and frame rate | The PC URP asset used full render scale, a 2048 main-light shadow map and a 50 m shadow distance. The Mobile asset used 0.8 render scale. `FootballGame.Awake` set a 60 FPS cap. | Players lacked an in-game way to trade detail for frame time on an 8 GB laptop. |

The baseline for this comparison is commit `49732b2`. The Unity version, URP configuration, input package, save schema, league and fixture systems were inspected alongside the gameplay code.

## Windows acceptance pass

Use Unity **6000.5.8f1**. Before testing New Season, keep a copy of `%USERPROFILE%\AppData\LocalLow\DefaultCompany\MiniFootball3D\mini-football-season.json` if you want to retain your season. Record the commit, GPU, resolution and preset with your results. These checks require the local Editor or Windows player.

| Check | Action | What to verify |
| --- | --- | --- |
| Upgrade and generation | Open the v2 branch over the playable checkout. Let compilation finish. Run **MiniFootball > Build Complete Game**, then **MiniFootball > Validate Generated Game**. Repeat. | Console reports successful validation. The generated scene appears once and first in the active build scene list. Existing scenes, asset references and modified open scenes survive regeneration. |
| Character import | Inspect the generated prefab and its humanoid Avatar. Watch both teams, including keepers and a substitute, from close-up and across the pitch. | Feet meet the pitch, the face and boots point forward, elbows and knees bend on the intended axes, and hair, skin tone, jersey number and shirt name update with the selected player. Mesh LOD switches preserve the silhouette. |
| Animation and physics | Start, stop, turn, walk, sprint and dribble. Pause the Editor and use **Step** through a pass, shot, tackle, keeper dive and high-ball challenge. | A planted foot stays near its contact point. Lower limbs bend throughout the stride. A pass or shot leaves on the strike. A moving receiver cushions the ball. Check ground contacts, separation, recovery from a tackle and keeper saves. |
| Controls and tactics | Aim passes in different directions. Tap K, then shoot with a full power meter. Track the selected player through a pass and Q switch. Substitute from the pause menu. | The intended action and selected teammate match the input. Sprint drains stamina and recovery restores it. Teammates offer width or support, a defender presses, others cover or mark, and the keeper distributes after recovery. |
| Restarts | At both ends, force a shot wide, a defensive deflection over the end line, and a last touch over the sideline by each team. Drive a shot into an outfielder near the line and check who touched the deflection last. Roll the ball along a line and strike each post. | The whole ball crosses before a restart. The last touch determines the correct goal kick, corner or throw-in. Verify placement, opposition clearance, release of the ball, and the match clock on returning to play. |
| Camera and replay | Score with replays on and off. Pause during a celebration. Change camera height from Match Settings. Follow a long pass and a near-touchline restart at 16:9 and after resizing. | The ball and controlled player stay readable. The replay shows the preceding play, credits the goal once and returns to the conceding team's kickoff. Check pitch markings, nets, stands, shadow transitions and camera stability. |
| League compatibility | Continue a known v1 save, finish a user fixture and relaunch. Exercise New Season with another save, manage the lineup and play through round seven. | Six-player squads, club identity, two free agents, prior results, standings and stats reload. Each round completes one user game and three simulations. The champion appears at season end. Leaving an incomplete match preserves the saved round. |
| Standalone | Build for Windows x86-64 and run outside the Editor. Change preset, frame-rate cap and replay setting. Relaunch and complete a match. | Menu, controls, visuals and persistence work in the build. Check `Player.log` next to the season save for shader, serialization, input or animation errors. |

## Measure the 8 GB laptop

Use the same resolution for each preset. Warm up the Windows standalone build, then record a full match at each combination of Low, Medium or High and 30 or 60 FPS. Include a crowded goalmouth, substitution, restart and replay. Repeat a match to check whether memory levels off.

Record median and 95th-percentile frame time, recurring spikes and peak process memory. Use a Development Build with Unity Profiler to attribute slow frames to CPU, GPU or garbage collection. Repeat the timing in a release build. The budgets are 33.3 ms at 30 FPS and 16.7 ms at 60 FPS. Laptop frame-time and memory measurements are pending.
