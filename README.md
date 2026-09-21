# PQS Bench

**⚠️ Work in progress.** This is an active investigation, not a finished mod. The figures, the code and the conclusions on this page can still change, and several questions are still open.

## What it is for

PQS Bench measures what building the KSP terrain costs, so that a mod patching it can be compared with
stock. It corrects nothing and changes nothing about the game: it only counts.

It times the vertex placement the game runs — whatever mod is patching it, or stock when none is —
against the stock placement, on the same quads, in the same run. It measures as soon as it is installed:
there is nothing to switch on, and nothing to switch off but taking its folder out of `GameData`.

It does not time frames. What the terrain costs a whole frame is a question for a frame profiler such as
[KSPProfiler](https://github.com/KSPModdingLibs/KSPProfiler), which times each phase of Unity's game loop.

It was written to check what [Terrain Precision Fix](https://github.com/lhervier/KSP-TerrainPrecisionFix)
costs, and it now measures other mods as well, such as
[Stock Quad Cache](https://github.com/lhervier/KSP-TerrainPrecisionFix-StockQuadCache). It knows nothing
about any of them: everything it hooks into is stock `PQS`, and it finds out what is patching the terrain
by asking Harmony.

**How this was made.** Written with Claude, Anthropic's AI assistant. Everything in it was reviewed and
validated by a human — me — who very much enjoyed learning along the way how KSP builds its terrain
quads. It is still a measuring instrument: a figure it produces is only worth the code that produced it,
so read that code before you trust the figure.

## How the bench works

### One hook

Everything the bench records comes from one event, **a quad is built**:

| when | what it gives |
|---|---|
| a Harmony prefix and postfix on `PQS.BuildQuad`: the loop over the vertices of one quad, then the `PQSMod`s told the quad is built (`OnQuadBuilt`), which is where the terrain scatter is given its quad | the sphere, the quad, how long the build took, and whether the quad is of the highest subdivision level. Raised only for a quad actually built, never for a call that built nothing |

The run itself starts on the first frame spent in flight after Alt+F7.

A quad **of the highest subdivision level** is one the game detaches into `LocalSpacePQStorage`: those
carry the collider a craft stands on, and the terrain scatter.

### Settings

`GameData/PQSBenchMod/PluginData/settings.cfg`, read once when KSP starts. To change it: quit KSP, edit
the file, start KSP again. It holds a single setting.

`logLevel` takes `Error`, `Warning`, `Info` (default), `Debug` or `Trace`. **Leave it at `Info`**: the
results are written at `Info`, so `Warning` or `Error` would silence them, and nothing in the mod writes
above `Info`.

### Keys

In flight:

- **Alt+F8** writes everything recorded so far to `KSP.log`.
- **Alt+F7** throws it away and starts again.

Alt stands for KSP's modifier key, whatever it is set to. Nothing is written until you ask for it: writing
while measuring would cost more than what is being measured. The recording lives as long as the game:
loading another save does not reset it, Alt+F7 does.

### What goes to `KSP.log`

Every line is prefixed with `[PQSBench]`, and every result is semicolon separated, for a spreadsheet or a
script.

**When KSP starts**, one line gives the version and the two keys. It is followed by a warning when
`logLevel` is above `Info`.

**On Alt+F8**, the dump opens with lines that let a log say for itself what it measured and where:

- `BENCH begin` — the Harmony ids patching `PQS.BuildVertexSurfaceRelative` and
  `PQS.BuildQuad`, or `none`. Read at dump time, since nothing says in which order mods install their
  patches.
- `BENCH machine` — the processor, its logical cores, the memory size, the graphics device KSP runs on and
  the operating system. Figures taken on two machines are not comparable, whatever else their logs agree
  on. The memory type is not in it (Unity does not expose it): write it down with the results.
- `BENCH run` — the save, the craft, the body it is flying over, the terrain detail preset, how many
  vertices a quad holds, and the stretch flown: `utStart`, `utEnd` and `utSpan` against `realSeconds`,
  with the altitude at both ends and the speed at the end. Then how many terrain quads the game built in
  flight along the way, `quads`, of which `topLevelQuads` of the highest subdivision level. The run starts
  on the first frame in flight after Alt+F7. The quad counts take in every sphere that builds quads: the
  Mun has one, and on a body with an ocean the ocean's quads are counted too.
- `BENCH sphere` and `BENCH colliders` — how the terrain of that body is set up, described
  [below](#how-the-terrain-of-that-body-is-set-up).

Then comes the `BENCH calibration` line, described below, and the dump closes on `BENCH end`.
**Alt+F7** writes `BENCH reset`.

**Two runs are comparable when their `BENCH run` lines agree.** Same save, same craft, same body, same
stretch of game time at the same altitude means the same ground was flown over twice, which is what
comparing their figures rests on. It is also where a run that warped shows up: `utSpan` far above
`realSeconds`. `topLevelQuads` says whether the run built the quads it was meant to measure at all, and
whether two runs built about as many.

### How the terrain of that body is set up

The dump's `BENCH sphere` line says what decides how far the sphere subdivides, and a `BENCH colliders`
line per `PQSMod_QuadMeshColliders` gives its `maxLevelOffset`. Only the sphere the craft is flying over
is described. Read on Kerbin and the Mun, KSP 1.12.5:

| sphere | minLevel | maxLevel | highest level appears under | lowest level with a collider | max angle per sample |
|---|---|---|---|---|---|
| Kerbin | 2 | 10 | 9 375 m | 10 | 4.60e-5 rad |
| Mun | 2 | 9 | 6 250 m | 9 | 9.20e-5 rad |
| KerbinOcean | 2 | 7 | 75 000 m | none | 3.68e-4 rad |

**The highest level also depends on how fast the game runs.** `PQ.UpdateSubdivision` only splits a quad
while `subdivision < sphereRoot.maxLevelAtCurrentTgtSpeed`, and that ceiling is worked out from how far
the craft moves between two samples of real time: the faster it flies, or the slower the game runs, the
lower it falls. `speedCapAt60Fps` is the ground speed above which it drops below `maxLevel` at 60 frames
per second. A run whose frames are slow enough for long enough builds fewer quads of the highest level,
which `topLevelQuads` shows.

## What is installed, against stock, on the same quads

The bench answers one question: what does placing **one terrain vertex** cost, and how much of that does
the installed mod change?

### Why the placement is replayed

Timing the game's own quad builds cannot answer it. A build does much more than place vertices, a game
only ever runs one placement, and the difference being looked for is small: from one KSP session to the
next, the whole game can run faster or slower by as much. The only comparison that holds is between
placements timed on the same quads, in the same session.

So the placement is replayed. It listens only to **a quad is built**, and only to quads of the highest
subdivision level: one in thirty-two of them, starting with the first. In the frame that just built it:

1. one untimed round of stock warms up the arrays the replay reads;
2. each of three placements is replayed over every vertex of the quad, **eight rounds each**, each round
   timed as a whole. A round over a whole quad lasts far longer than the clock's resolution: the eight
   rounds are there to average, not to make the measurement possible;
3. the order of the three rotates from one quad to the next, so that each runs as often first as last;
4. the quad, and everything of `PQS` the replay wrote to, are put back exactly as they were found.

### What is replayed

| | |
|---|---|
| `installed` | `PQS.BuildVertexSurfaceRelative` itself, so it runs through whatever Harmony patch is on it — or straight to stock when there is none. **The bench does not know, and does not need to know, which mod that is** |
| `stock` | the stock placement, reached through a Harmony reverse patch, which copies the original method's IL into a stub. It therefore stays measurable in a run where the stock method is patched, without being a transcription that could drift from the game — or be faster than it, which a transcription of those four lines is: stock keeps its intermediate values in fields of `PQS` and reads its inputs through a field, where a copy would use locals. It is the yardstick each run is read against |
| `harness` | places nothing. What it measures is **the fixed cost of the replay itself** — the fields written before each call, and the indirect call — which the two others also pay, and which is subtracted from both |

### Two things the replay has to reproduce

**Where a placement reads its inputs.** `PQS.BuildVertexSurfaceRelative` ignores the `VertexBuildData`
it is handed and reads `vbData`, `vertexIndex` and `buildQuad`, three fields of `PQS`. The replay sets
them for every vertex, identically for every placement — which is the cost `harness` is there to measure.

**A quad it has not seen.** A placement that works something out once per quad, and reuses it for that
quad's couple of hundred vertices, only pays for it once. Replay eight rounds without saying so and seven
of them ride free, which flatters the cache by a factor of eight. So before each timed round, and outside
the clock, the bench re-enters `PQS.BuildQuad` on the quad: stock turns an already built quad away on the
method's first line, before touching anything — but the Harmony prefixes have run by then.

That is the one thing a mod has to do to be measured honestly here:

> **A mod that keeps state per terrain quad must invalidate it on a `PQS.BuildQuad` prefix.**

It is not a rule invented for this: a quad can be rebuilt after having been moved, so anything worked out
from it is stale at that point anyway. If re-entering `PQS.BuildQuad` ever builds the quad instead of
turning it away, the calibration stops for the rest of the game and says so in `KSP.log`.

### What comes out

One `BENCH calibration` line, over the quads calibrated since the last Alt+F7:

| column | |
|---|---|
| `quads`, `roundsPerQuad`, `verticesPerFormula` | how much was timed: each placement ran over `verticesPerFormula` vertices |
| `stockNsPerVertex`, `installedNsPerVertex` | what placing one vertex costs, **net of the harness** |
| `differenceNsPerVertex` | installed minus stock: **the figure to read** |
| `harnessNsPerVertex` | the fixed cost of the replay, taken off both |
| `stockRawNsPerVertex`, `installedRawNsPerVertex` | the two before subtraction, so that it can be checked |

**Read `differenceNsPerVertex` within one run**, never by subtracting the `installedNsPerVertex` of two
runs: that would add up the noise of two sessions, which is what the replay is there to avoid.

When nothing was calibrated, the line says `quads=0` and why.

## Measuring a terrain mod

The flight is on rails, so loading the same save twice covers the same ground twice. A run with the mod
being measured installed, against a run with its folder taken out of `GameData`, is the whole method —
and taking it out is the only honest reference: a mod left in place with its correction switched off
still pays for its own patches on the path being timed.

One run measures one configuration, since `GameData` is read once at startup. The bench only replays
the vertex placement: for a mod that does not patch `PQS.BuildVertexSurfaceRelative`, it has nothing to
tell, and a frame profiler is the measurement.

**The bench does real work inside the frames it runs in**: never time frames — with a profiler or
anything else — in a run where it is installed. Take its folder out of `GameData` for those.

### The game and the machine

Set once, before the first run, and left alone until the last one:

- **Vertical sync off** (in KSP's `settings.cfg`: `SYNC_VBL = 0`), and no frame limit
  (`FRAMERATE_LIMIT`). With either, the frame rate follows the screen or the setting, and the frame rate
  decides how far the terrain subdivides (see [above](#how-the-terrain-of-that-body-is-set-up)): the
  quads built would depend on the screen rather than on the game.
- **The same terrain detail preset** in every run. It decides how far the sphere subdivides, and the
  `BENCH run` line gives it.
- **The machine in the same state**: on mains power and at maximum performance for a laptop, nothing
  heavy running alongside, and KSP forced onto one graphics device when there are two. The
  `BENCH machine` line gives the processor and the graphics device; write the memory type down with the
  results, the log cannot.

### The save

**The save the reference runs were flown from is provided**:
[`perfs/ref-mune-5km.sfs`](perfs/ref-mune-5km.sfs), a sandbox game saved in KSP 1.12.5, holding one Mk1
command pod in a circular equatorial orbit 5 km over the Mun. To use it, copy it into the folder of a sandbox
game in `saves`, and load it from that game (Alt+F9). Its craft is called `Vaisseau sans nom`, which is what the `BENCH run` lines will say.

Flying from that save covers the same ground as the runs in [`perfs/`](perfs/). The steps that made it
follow, for another altitude or another body.

### Making the save yourself

1. A new craft carrying **a command pod and nothing else**. Any craft works; one part keeps it obvious.
2. Launch it. From the VAB or the SPH, it makes no difference.
3. Debug menu (Alt+F12) → *Cheats* → *Set Orbit*. Pick the Mun, then set **Semi-Major Axis to 205000**
   and leave every other field at 0. The semi-major axis is measured from the **centre of the body**,
   not from the ground: 5 km up is the Mun's 200 km radius plus 5 000, so 205 000. Zero eccentricity and
   zero inclination give a circular equatorial orbit, which keeps the craft at that one altitude. Tick
   the box that skips the safety checks, then *Set Orbit*.

   ![Set Orbit, with a semi-major axis of 205 000 m](imgs/00-set-orbit.png)

4. Save. The craft is now 5 000 m up, crossing the ground at 554 m/s:

   ![The craft in a 5 km orbit of the Mun](imgs/10-mun-orbit.png)

The screenshots are from a French install; the fields are in the order above whatever the language.

**It takes some luck.** The Mun's equator rises above 5 km in places: that orbit does not go all the way
round, and the craft crashes before it does. A save is only usable if the couple of minutes a run flies
from it clear the ground, so it may take a few tries — which is why the provided one is worth using.

5 km over the Mun is a compromise: below the 6 250 m the highest level needs, and low enough that about
40 % of the quads built are top level ones. On another body, read `highestLevelUnder` in the log first
and aim well below it.

### The runs

Each run is a fresh KSP — `settings.cfg` is only read at startup, and so is `GameData`. **Copy `KSP.log`
between two runs**, KSP overwrites it at every start.

1. Load the save.
2. Turn the camera so that part of the ground is in view, the same way in every run, and leave it there.
   The game does not place the camera the same way at every load, and what it shows changes what a frame
   costs to draw: with a different view, the frame rate, and with it how far the terrain subdivides,
   would follow the view.
3. Look at the mission time in flight, and pick a round value a little ahead of it: 30 s, for instance.
4. When it reads that value, **Alt+F7**. The scene load is then out of the recording.
5. A set time later, 2 min 30 s for instance, at ×1 all along, **Alt+F8**.

**These two marks, the same in every run, are what makes the flights reproducible and comparable.** The
craft is on rails: starting and stopping at the same mission time means flying over the same stretch of
ground, and building the same quads. The `utStart` and `utSpan` of the `BENCH run` lines are there to
check it.

A run is worth keeping when the `quads` of its `BENCH calibration` line is above zero. Its `BENCH run`
and `BENCH begin` lines are what says it was the run you meant to fly, with the mods you meant to have
installed.

## What stock costs

Two runs of KSP 1.12.5 with nothing patching the terrain, flown from the provided save by the procedure
above. **Taken on my desktop**: Intel Core i7-4790K, 32 GB of DDR3, NVIDIA GeForce GTX 1060, Windows 10.
Figures from another machine are not comparable to these.

| | run 1 | run 2 |
|---|---|---|
| placing a vertex (`stockNsPerVertex`) | 284.2 ns | 284.7 ns |
| `differenceNsPerVertex`, with nothing installed | −2.5 ns | +3.4 ns |

With nothing installed, the two calibrated placements are the same code reached two different ways:
their difference is the floor of the method, what any run carries of the measurement itself — about
3 ns on this machine, one way or the other.

The logs, the machine in full, and how each figure is read out of them are in [`perfs/`](perfs/).

## Install

Requires KSP 1.12 and [HarmonyKSP](https://github.com/KSPModdingLibs/HarmonyKSP).

Copy `GameData/PQSBenchMod` into the `GameData` of KSP. Nothing is written to your saves. **It measures
as soon as it is installed**, and its replay costs time in the frames that build terrain: take it out of
`GameData` when you are not measuring.

## Build

Set `KSPDIR` to your KSP install folder, which must contain `GameData/000_Harmony`, and run `build.bat`.
It needs the .NET SDK, and produces `GameData/PQSBenchMod/PQSBenchMod.dll`.

## Licence

MIT, see [LICENSE](LICENSE).
