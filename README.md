# Junction Rules

A Cities: Skylines II mod that makes traffic behave more like real (UK-style) traffic at junctions, crossings and
building driveways, and adds painted **yellow box junctions** that drivers actually obey.

Status: 1.3.0, on [Paradox Mods](https://mods.paradoxplaza.com/mods/168032/Windows), tested on a left-hand-traffic city. Expect rough edges.

**Getting started:** Options → Junction Rules → set **Car driving** to **Junction Rules** (it starts on the game's own
driving, so nothing changes until you switch); for the whole-junction gap rule also set **Junction gap rule** to
**Whole junction**.

## How it works

The game's car-driving system is switched off and replaced by a Burst-compiled copy of it with Junction Rules'
changes inside. The copy is checked against the game's own code at load: if the game updates and the code no longer
matches, the mod falls back to the game's driving. Turning **Car driving** back to the game's own in the settings
restores stock behaviour at any time.

## Junction rules

**Whole-junction gap.** A car that has to give way stops at one spot, just before the first place its path crosses
another lane, and decides once for the whole junction: it goes only when every conflict on its path is clear for the
time it needs to get through, judged on the speed and distance of approaching vehicles. It never creeps into a
junction and stops halfway across another lane to give way. Vehicles stopped at a red light don't count as
approaching.

**Turning across oncoming traffic gives way.** Straight-ahead traffic has priority over vehicles turning across it
(right turns in left-hand traffic, left turns in right-hand traffic).

**Pedestrians and zebras.**
- At a zebra, people have priority: cars never push through people on the crossing.
- If a car does end up blocking a crossing, people walk past it after a short wait (2 s by default), so nobody is
  trapped.
- Cars don't stop on a zebra unless there's room past it for the whole vehicle.
- At unmarked crossings, people give way to cars instead of cars giving way to people.
- Cars only wait for people actually crossing, not for someone standing at the kerb.

**Leaving and entering buildings.**
- A vehicle leaving any building treats the pavement like a zebra: it stops before the pavement and pulls out only
  when people, the lanes it crosses and the lane it joins are all clear, with room for its whole length.
- A vehicle turning into a building across oncoming traffic gives way to that traffic.

**Unboxed junctions** behave like normal junctions: a turning vehicle may enter and wait inside for space in its
exit, as long as it isn't standing across moving traffic. Holding cars back for lack of space past the junction is
the job of a yellow box.

## Yellow boxes

A yellow box marks an area drivers must not stop in.

**Placing boxes.**
- Open the tool with **Ctrl+Y** or the toolbar button.
- Hover a junction or a building to see an outline: white means it will be added, orange means it will be removed.
- Click to add or remove. Boxes are saved with your city.
- Each real car access of a building (exit, entrance or two-way) is boxed on its own: hover the access you want and
  click; an arrow shows which access a box belongs to.
- An access lights up with the cursor anywhere on its box area on the road, or on the pavement, driveway or verge
  between the road and the building's access; where two overlap, the nearer one wins.
- If a spot can't take a box (elevated or tunnel roads, highways, roundabouts and so on), the tool says why.

**How boxes are drawn.**
- Straight sides, kerb to kerb.
- Never over zebras, give-way or stop lines, keeping a 0.5 m gap.
- Never over a median.
- Boxes close together, nearer than a car length, merge into one larger box.
- Other road markings under a box can optionally be hidden.
- No parking inside a box.

**How drivers obey them.**
- A vehicle enters a box only if there is room past it for its whole length (trailers included): free right now, or, with the setting Look past the car in front at a box (on by default), free by the time it gets there, with every vehicle ahead expected to keep going and to stop only behind a queue or vehicle it can see standing, or at a red light (within its 100 m line of sight; nothing slow in sight means it goes). The vehicles ahead are packed nose to tail, never in another yellow box, on a zebra (except a signal zebra whose people have red) or in another junction.
  A queue creeping slower than about 18 km/h (5 m/s) counts as standing.
- If it can't clear the box, it stops with its front bumper at the line, not in the box.
- Several boxes close together are judged per vehicle: a gap between boxes only counts if that vehicle fits in it.
- **Turning across oncoming traffic** (UK exception, optional): a turner may wait inside a junction box when its exit
  is clear and only oncoming traffic stops it.
- **Same-exit-lane exception:** a vehicle from a give-way approach may enter even when its exit is full, if where it
  would wait only blocks traffic heading for that same full lane. Building exits count as give-way approaches.
- Vehicles turning into a building wait before the box until the driveway has room.

The mod never adds a starvation timeout: a vehicle that can't go legally waits. Turn the individual rules on or off
in the settings to suit your city.

## For other mods and tools

Junction Rules exposes a small public API, `JunctionRules.Api.JunctionRulesApi` (v1: List / Preview / Add / Remove,
JSON in and out), so other mods can list, preview, add and remove yellow boxes through exactly the same rules as the
Ctrl+Y tool. Find it by reflection (no compile-time reference needed) and check `ApiVersion` before calling.

**Why is this car slowing?** Select a car to see why it slows. While a car is selected (and Junction Rules drives the
cars), every change of reason goes to `Logs/JunctionRules.Mod.log` as a `why slowed:` line (at most 2 a second): the
rule that set its speed limit (the game's own, e.g. a light, a STOP sign, the car ahead, giving way, or a Junction
Rules rule, e.g. the whole-junction gap, a yellow box or a zebra), the limit, the blocker and how far away the car is.
A limit no rule can be named for is shown as "stock/other", never as a Junction Rules rule. Tools can read the same
through `JunctionRulesApi.WhySlowed(argsJson)`: the current reason plus the last 20 changes of the tracked car;
`{"car":"index:version"}` tracks a car while nothing is selected (a selected vehicle always wins), `{"clear":true}` stops
that, `{"history":"index:version"}` reads a recently tracked car's changes.

## Settings

Options → Junction Rules. The main groups:
- **Car driving:** the game's own or Junction Rules; the junction gap rule; turning across oncoming traffic; crossing
  and zebra rules; building exits give way; people give way at unmarked crossings.
- **Yellow boxes:** the tool shortcut; cars obey yellow boxes; the turning-across exception; no parking in boxes;
  hiding markings under boxes; the gap to road markings.
- **Developer options** (hidden; shown only when the game is started with the `-developerMode` launch option): timing
  log, exit trace, "watch the junction at camera centre" (all write to `Logs/JunctionRules.Mod.log`), skip code hash
  check, and the old yellow box paint test. All off by default.

## Compatibility

- Saves stay loadable without the mod: boxes are stored as small components that the game skips when the mod is
  missing.
- Works alongside Traffic (lane connections, priorities) and RoadBuilder roads.
- Built against game version 1.6.4. After a game update the mod checks its copy of the driving code and falls back to
  the game's own driving if they no longer match.

## Building

.NET SDK with the Cities: Skylines II modding toolchain:
`dotnet build -c Release -p:Burst=true`. Tests: `dotnet test tests/JunctionRules.Tests`. The copied game code is
checked with `python3 tools/vanilla_copy.py check`.

Development notes, probe logs and design history are in `docs/` and the `PROBE-*.md` files.

## Publishing to Paradox Mods

The official mod template's files: `JunctionRules/Properties/PublishConfiguration.xml` (texts, images, version) and the
three profiles in `JunctionRules/Properties/PublishProfiles/`. Run from the `JunctionRules/` folder with the game closed
(the Burst build and ModPublisher both use the game's Proton prefix); each profile builds with Burst first and uploads
`bin/PublishContent/JunctionRules`. ModPublisher signs in with the Paradox account the game last signed in with; without
one, add `-p:ModPublisherExtraArgs="--noAutoLogin -l /absolute/path/login.txt"` (email on line 1, password on line 2;
keep that file outside the repo).

- First upload: `dotnet publish -c Release -p:PublishProfile=PublishNewMod`, then put the printed
  `Mod published with Id=NNNNN` into `<ModId Value="NNNNN" />` and commit it.
- A new version: bump `<Version>`/`<InformationalVersion>` in the csproj and `<ModVersion>` (tests keep them equal),
  write `<ChangeLog>`, then `dotnet publish -c Release -p:PublishProfile=PublishNewVersion`.
- Texts or images only: `dotnet publish -c Release -p:PublishProfile=UpdatePublishedConfiguration`.
- Add `-p:PublishDryRun=true` to any of them to have ModPublisher check the configuration fields and stop before signing in
  (add `-p:Burst=false` too for a quick check without the Burst build; image files are checked by the tests, not by
  ModPublisher, which silently uses its own thumbnail when the file is missing).

## License

MIT for Junction Rules' own code. `JunctionRules/Vanilla/` is adapted from decompiled Cities: Skylines II code and
remains the property of Colossal Order / Paradox Interactive. See `LICENSE`.
