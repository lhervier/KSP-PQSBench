# PQS Bench

Measures what building the KSP terrain costs, in flight: how many quads the game builds per second, how
long each one takes, and how much of a frame that is. It corrects nothing and changes nothing about the
game — it only counts.

It was written to check what
[Terrain Precision Fix](https://github.com/lhervier/KSP-TerrainPrecisionFix) costs, but it knows nothing
about that mod, or any other. Everything it measures is stock `PQS`, and `calibrate` times whatever is
patching the vertex placement without needing to know what that is.

## Install

Requires KSP 1.12 and [HarmonyKSP](https://github.com/KSPModdingLibs/HarmonyKSP) (the usual
`GameData/000_Harmony`, also installed by KSP Community Fixes).

Copy `GameData/PQSBenchMod` into the `GameData` of KSP. Nothing is written to your saves.

**With `benchMode = off`, which is how it ships, no Harmony patch is applied at all**: the mod is inert,
and leaving it installed costs nothing.

## Settings

`GameData/PQSBenchMod/PluginData/settings.cfg`, read once when KSP starts. To change it: quit KSP, edit
the file, start KSP again.

| `benchMode` | what is recorded |
|---|---|
| `off` (default) | nothing, and nothing is patched |
| `counters` | what the terrain costs in flight, one line per second of game time |
| `calibrate` | what one terrain vertex costs, installed against stock — see below |

**The two measuring modes are exclusive**, and one run measures one of them. `calibrate` does real work
inside the very frames `counters` times, so a mode that ran both would publish frame times it had itself
inflated. In `calibrate` the counters are not merely ignored: nothing records them. The patch that times
a frame is still installed, and reads the clock with no one listening, outside anything `calibrate`
times.

`logLevel` takes `Error`, `Warning`, `Info` (default), `Debug` or `Trace`. **Measure at `Info`**: the
measurement itself writes at `Info`, and anything above it makes other mods write to `KSP.log` on the
very path being timed.

In flight:

- **Alt+F8** writes everything recorded so far to `KSP.log`.
- **Alt+F7** throws it away and starts again.

Nothing is written until you ask for it: writing while measuring would cost more than what is being
measured. The recording is static — loading another save does not reset it, Alt+F7 does.

## What comes out

Everything is semicolon separated, for a spreadsheet or a script.

### Which run it is

Every dump opens the same way whatever the mode, so that a log says for itself what it measured and
where:

- `BENCH begin` — the mode, and the Harmony ids patching `PQS.BuildVertexSurfaceRelative` and
  `PQS.BuildQuad`. Read at dump time, since nothing says in which order mods install their patches.
- `BENCH run` — the save, the craft, the body it is flying over, the terrain detail preset, how many
  vertices a quad holds, and the stretch flown: `utStart`, `utEnd` and `utSpan` against `realSeconds`,
  with the altitude at both ends and the speed at the end. The run starts on the first frame in flight
  after Alt+F7.
- `BENCH sphere` and `BENCH colliders` — how the terrain of that body is set up, below.

**Two runs are comparable when their `BENCH run` lines agree.** Same save, same craft, same body, same
stretch of game time at the same altitude means the same ground was flown over twice, which is what
comparing their figures rests on. It is also where a run that warped shows up: `utSpan` far above
`realSeconds`.

### `counters`: one line per second of game time

Time warp is skipped entirely: the craft crosses the ground far too fast for a sample to mean anything.
A `BENCH counters` line gives how many samples were kept and how many seconds were dropped to warp,
then one line per sample:

| column | |
|---|---|
| `ut`, `utSpan` | when the sample was taken, and how much game time it covers |
| `realSeconds`, `frames`, `fps` | and how much real time that was |
| `altitude`, `speed` | where the craft was at the end of it |
| `quads`, `vertices` | terrain quads actually built during that second |
| `topLevelQuads` | of which quads of the highest subdivision level — the ones the game detaches into `LocalSpacePQStorage`, which carry the collider craft stand on |
| `buildMs`, `topLevelBuildMs` | what `PQS.BuildQuad` spent on them |
| `updateMs` | what the whole terrain update of the sphere cost that second: the quad builds, plus the subdivision decisions and the normals. This is what a frame pays |
| `subdivisionAvg`, `subdivisionMax` | the levels those quads were at |
| `speedLevelCap`, `maxLevel` | the ceiling the game put on subdivision, and the sphere's own maximum |

### How the terrain of that body is set up

A `BENCH sphere` line says what decides how far the sphere subdivides, and a `BENCH colliders` line per
`PQSMod_QuadMeshColliders` gives its `maxLevelOffset`. Only the sphere the craft is flying over is
described — a body with an ocean has a second one, which builds quads too. Read on Kerbin and the Mun,
KSP 1.12.5:

| sphere | minLevel | maxLevel | highest level appears under | lowest level with a collider | max angle per sample |
|---|---|---|---|---|---|
| Kerbin | 2 | 10 | 9 375 m | 10 | 4.60e-5 rad |
| Mun | 2 | 9 | 6 250 m | 9 | 9.20e-5 rad |
| KerbinOcean | 2 | 7 | 75 000 m | none | 3.68e-4 rad |

**`speedLevelCap` is the column to watch.** `PQ.UpdateSubdivision` only splits a quad while
`subdivision < sphereRoot.maxLevelAtCurrentTgtSpeed`, and that ceiling is worked out from how far the
craft moves between two samples of real time: the faster it flies — or the slower the game runs — the
lower it falls. A run where `speedLevelCap` sits below `maxLevel` never built the quads you were trying
to measure.

## `calibrate`: what is installed, against stock, on the same data

`counters` measures the game as it runs. `calibrate` answers a narrower question: what does **one
terrain vertex** cost, and how much of that does the mod under test change?

A vertex costs around a hundred nanoseconds, and a `Stopwatch` tick is a hundred nanoseconds, so the
placement has to be replayed. On one quad in thirty-two, in the frame that just built it, each of three
things is run over the quad's vertices, eight rounds each, and the quad is then put back exactly as it
was found. The order rotates from quad to quad, so each runs as often first as last.

| | |
|---|---|
| `installed` | `PQS.BuildVertexSurfaceRelative` itself, so it runs through whatever Harmony patch is on it — or straight to stock when there is none. **The bench does not know, and does not need to know, which mod that is** |
| `stock` | the stock placement itself, reached through a Harmony reverse patch, which copies the original method's IL into a stub. It therefore stays measurable in a run where the stock method is patched, without being a transcription that could drift from the game — or be faster than it, which a transcription of those four lines is: stock keeps its intermediate values in fields of `PQS` and reads its inputs through a field, where a copy would use locals. It is the yardstick two runs are compared through |
| `harness` | places nothing. What it measures is what the replay itself costs — the fields written before each call, and the indirect call — which the two others also pay, and which is subtracted from both |

The dump gives `stockNsPerVertex` and `installedNsPerVertex` **net of the harness**, their difference,
and the three raw figures so that the subtraction can be checked.

### Two things the replay has to reproduce

**Where a placement reads its inputs.** `PQS.BuildVertexSurfaceRelative` ignores the `VertexBuildData`
it is handed and reads `vbData`, `vertexIndex` and `buildQuad`, three fields of `PQS`. The replay sets
them for every vertex, identically for every formula — which is the cost `harness` is there to measure.

**A quad it has not seen.** A placement that works something out once per quad, and reuses it for that
quad's couple of hundred vertices, only pays for it once. Replay eight rounds without saying so and
seven of them ride free, which flatters the cache by a factor of eight. So before each timed round, and
outside the clock, the bench re-enters `PQS.BuildQuad` on the quad: stock turns an already built quad
away on the method's first line, before touching anything — but the Harmony prefixes have run by then.

That is the one thing a mod has to do to be measured honestly here:

> **A mod that keeps state per terrain quad must invalidate it on a `PQS.BuildQuad` prefix.**

It is not a rule invented for this: a quad can be rebuilt after having been moved, so anything worked
out from it is stale at that point anyway.

### What the dump says about the placement

`differingQuads` counts the calibrated quads where the installed placement put a vertex somewhere other
than stock does, compared exactly. Zero means either that nothing is patching the placement, or that
what is changes what it costs without changing the terrain.

## What stock costs

A reference run of a KSP with nothing patching the terrain is kept in [`perfs/`](perfs/), with its logs:
what a quad costs to build, what a vertex costs to place, and what share of a frame the terrain takes,
5 km over the Mun. It is also where this mod's own accuracy is checked, since with nothing installed the
two calibrated figures are the same code reached two different ways.

## Measuring a terrain mod

The flight is on rails, so loading the same save twice covers the same ground twice. A run with the mod
being measured installed, against a run with its folder taken out of `GameData`, is the whole method —
and taking it out is the only honest reference: a mod left in place with its correction switched off
still pays for its own patches on the path being timed.

One run measures one configuration in one mode, since both `GameData` and `settings.cfg` are read once
at startup: a configuration takes two runs, one `counters` and one `calibrate`. What makes
the runs comparable is that each of them carries its own `stock` yardstick, measured in the same frames
as the thing under test: read `installedNsPerVertex` against the `stockNsPerVertex` of **its own run**,
not against another machine's.

### The craft

1. A new craft carrying **a command pod and nothing else**. Any craft works; one part keeps it obvious.
2. Launch it. From the VAB or the SPH, it makes no difference.
3. Debug menu (Alt+F12) → *Cheats* → *Set Orbit*. Pick the Mun, then set **Semi-Major Axis to 205000**
   and leave every other field at 0. The semi-major axis is measured from the **centre of the body**,
   not from the ground: 5 km up is the Mun's 200 km radius plus 5 000, so 205 000. Zero eccentricity and
   zero inclination give a circular equatorial orbit, which keeps the craft at that one altitude and
   away from the higher ground off the equator. Tick the box that skips the safety checks, then
   *Set Orbit*.

   ![Set Orbit, with a semi-major axis of 205 000 m](imgs/00-set-orbit.png)

4. Save. The craft is now 5 000 m up, crossing the ground at 554 m/s:

   ![The craft in a 5 km orbit of the Mun](imgs/10-mun-orbit.png)

The screenshots are from a French install; the fields are in the order above whatever the language.

5 km over the Mun is a compromise: below the 6 250 m the highest level needs, high enough not to hit a
ridge, and low enough that about 40 % of the quads built are top level ones. On another body, read
`highestLevelUnder` in the log first and aim well below it.

### The runs

Each run is a fresh KSP — `settings.cfg` is only read at startup, and so is `GameData`. **Copy `KSP.log`
between two runs**, KSP overwrites it at every start.

Load the save, **Alt+F7** once in flight so that the scene load is not in the samples, fly a couple of
minutes at ×1, then **Alt+F8**. Use the same two marks in every run: the numbers are rates and ratios,
but two runs are only comparable if they cover the same stretch of orbit — which is what the `utStart`
and `utSpan` of their `BENCH run` lines are there to show.

A `counters` run is worth keeping when `topLevelQuads` is above zero and `speedLevelCap` sits at
`maxLevel`; a `calibrate` run, when `quads` is above zero. In both, the `BENCH run` and `BENCH begin`
lines are what says the run was the one you meant to fly, with the mods you meant to have installed.

## Build

Set `KSPDIR` to your KSP install folder, which must contain `GameData/000_Harmony`, and run `build.bat`.
It needs the .NET SDK, and produces `GameData/PQSBenchMod/PQSBenchMod.dll`.

## How this was made

Written with Claude, Anthropic's AI assistant. I am saying so because it is true, and because it is a
measuring instrument: every figure it produces is only worth what the code that produced it is worth, so
read it before you trust it.

## Licence

MIT, see [LICENSE](LICENSE).
