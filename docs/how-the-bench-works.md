# How the bench works

Part of [PQS Bench](../README.md). The short version is on the main page, under
[How the bench works](../README.md#how-the-bench-works); here are the hook it listens to, its window, and
every line it writes to `KSP.log`.

## One hook

Everything the bench records comes from one event, **a quad is built**:

| when | what it gives |
|---|---|
| a Harmony prefix and postfix on `PQS.BuildQuad`: the loop over the vertices of one quad, then the `PQSMod`s told the quad is built (`OnQuadBuilt`), which is where the terrain scatter is given its quad | the sphere, the quad, how long the build took, and whether the quad is of the highest subdivision level. Raised only for a quad actually built, never for a call that built nothing |

The run itself starts on the first frame spent in flight after *Reset*.

A quad **of the highest subdivision level** is one the game detaches into `LocalSpacePQStorage`: those
carry the collider a craft stands on, and the terrain scatter.

## The window

In flight, a small window holds two buttons:

- **Dump to KSP.log** writes everything recorded so far to `KSP.log`.
- **Reset** throws it away and starts again.

`Alt+F6` shows or hides the window; Alt stands for KSP's modifier key, whatever it is set to. Nothing is
written until you ask for it: writing while measuring would cost more than what is being measured. The
recording lives as long as the game: loading another save does not reset it, *Reset* does.

## What goes to `KSP.log`

Every line is prefixed with `[PQSBench]`, and every result is semicolon separated, for a spreadsheet or a
script.

**When KSP starts**, one line gives the version and the two buttons. It is followed by a warning when
`logLevel` is above `Info` (see [Settings](../README.md#settings)).

**On *Dump to KSP.log***, the dump opens with lines that let a log say for itself what it measured and where:

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
  on the first frame in flight after *Reset*. The quad counts take in every sphere that builds quads: the
  Mun has one, and on a body with an ocean the ocean's quads are counted too.
- `BENCH sphere` and `BENCH colliders` — how the terrain of that body is set up, described
  [below](#how-the-terrain-of-that-body-is-set-up).

Then comes the `BENCH calibration` line, described in
[What comes out](what-is-installed-against-stock.md#what-comes-out), and the dump closes on `BENCH end`.
***Reset*** writes `BENCH reset`.

**Two runs are comparable when their `BENCH run` lines agree.** Same save, same craft, same body, same
stretch of game time at the same altitude means the same ground was flown over twice, which is what
comparing their figures rests on. It is also where a run that warped shows up: `utSpan` far above
`realSeconds`. `topLevelQuads` says whether the run built the quads it was meant to measure at all, and
whether two runs built about as many.

## How the terrain of that body is set up

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
