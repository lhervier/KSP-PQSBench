# PQS Bench

Measures what building the KSP terrain costs, in flight: how many quads the game builds per second, how
long each one takes, and how much of a frame that is. It corrects nothing and changes nothing about the
game — it only counts.

It was written to check what
[Terrain Precision Fix](https://github.com/lhervier/KSP-TerrainPrecisionFix) costs, and it carries one
measurement specific to that mod (`calibrate`, below). Everything else is about stock `PQS` and works
with any mod, or none.

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
| `calibrate` | adds the two vertex placements timed against each other — see below |

`logLevel` takes `Error`, `Warning`, `Info` (default), `Debug` or `Trace`. **Measure at `Info`**: the
measurement itself writes at `Info`, and anything above it makes other mods write to `KSP.log` on the
very path being timed.

In flight:

- **Alt+F8** writes everything recorded so far to `KSP.log`.
- **Alt+F7** throws it away and starts again.

Nothing is written until you ask for it: writing while measuring would cost more than what is being
measured. The recording is static — loading another save does not reset it, Alt+F7 does.

## What comes out

One line per second of game time, semicolon separated, for a spreadsheet or a script. Time warp is
skipped entirely: the craft crosses the ground far too fast for a sample to mean anything.

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

Plus, once per terrain sphere, a `BENCH sphere` line with what decides how far it subdivides, and a
`BENCH colliders` line per `PQSMod_QuadMeshColliders` with its `maxLevelOffset`. Read on Kerbin and the
Mun, KSP 1.12.5:

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

## `calibrate`: three formulas on the same data

`counters` measures the game. `calibrate` answers a narrower question: of several ways of placing a
terrain vertex, which is faster on this machine, and where does the difference come from?

On one quad in thirty-two, in the frame that just built it, each placement is replayed over its
vertices, eight rounds each, and the quad is put back exactly as it was found. The order rotates from
quad to quad, so each formula runs as often first as last. The three are:

| | |
|---|---|
| `stock` | as `PQS.BuildVertexSurfaceRelative` does it: `Transform.TransformPoint` then `Transform.InverseTransformPoint`, **and a read of `Component.transform` before each** — that method is called once per vertex, and reads `base.transform` and `buildQuad.transform` every time |
| `stockHoisted` | the same arithmetic, with the two `Transform`s read once per quad instead. Not a placement the game contains: it exists to separate the cost of the arithmetic from the cost of asking Unity for a `Transform` |
| `fixed` | the one Terrain Precision Fix puts in their place, if that mod is installed — the real method, not a copy of its arithmetic, so what a vertex costs includes everything that mod works out along the way |

Two differences, each between formulas that differ by one thing only, and both reported in the dump:

- `stock` − `stockHoisted` = **`transformReadsNsPerVertex`**, what reading the two `Transform`s costs;
- `fixed` − `stockHoisted` = **`arithmeticNsPerVertex`**, what the double-precision arithmetic costs
  against stock's, both being organised the same way: worked out once per quad, tested for per vertex.

The fix's method is `private` to the other mod, which exposes nothing for this on purpose. It is reached
through a delegate bound once, which costs an indirect call per vertex — **so the two stock formulas are
put behind delegates of the same type**, bound the same way, and all three are preceded by a reset of
the same type (plain stock has nothing to reset). The calibration then compares formulas rather than
ways of reaching one of them.

Without Terrain Precision Fix installed, `fixed` is reported as `n/a` and the two stock formulas are
still timed — what a `Transform` read costs has nothing to do with that mod. If the fix is installed but
declines a quad, that quad is skipped and counted in `refusedQuads` rather than timed against an empty
loop.

## Measuring a terrain mod

The flight is on rails, so loading the same save twice covers the same ground twice. A run with the mod
being measured installed, against a run with its folder taken out of `GameData`, is the whole method —
and taking it out is the only honest reference: a mod left in place with its correction switched off
still pays for its own patches on the path being timed.

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
but two runs are only comparable if they cover the same stretch of orbit.

A run is worth keeping when `topLevelQuads` is above zero and `speedLevelCap` sits at `maxLevel`. The
`BENCH begin` line records whether Terrain Precision Fix was there and whether it was actually patching,
so a log says for itself which run it is.

## Build

Set `KSPDIR` to your KSP install folder, which must contain `GameData/000_Harmony`, and run `build.bat`.
It needs the .NET SDK, and produces `GameData/PQSBenchMod/PQSBenchMod.dll`.

## How this was made

Written with Claude, Anthropic's AI assistant. I am saying so because it is true, and because it is a
measuring instrument: every figure it produces is only worth what the code that produced it is worth, so
read it before you trust it.

## Licence

MIT, see [LICENSE](LICENSE).
