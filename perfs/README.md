# What the stock terrain costs

A reference run of stock KSP, with no mod touching the terrain at all: what building a quad costs, what
placing a vertex costs, and what a frame pays for it. It is the yardstick any measurement of a terrain
mod is read against, and it is here because it says nothing about any mod — only about the game.

The procedure that produced it is on [this mod's page](../README.md): the craft, the orbit, how long to
fly, and what makes a run worth keeping. What follows is what it produced.

## The run

KSP 1.12.5. `GameData` holding Harmony, ModuleManager, KSP Community Fixes and this
measuring mod — **nothing patching the terrain**. A command pod on rails in a circular orbit 5 km over
the Mun, flown for 150 seconds of game time. The save is kept next to this page:
[`ref-mune-5km.sfs`](ref-mune-5km.sfs).

### The machine

| | |
|---|---|
| processor | Intel Core Ultra 7 155H, a laptop's, 22 logical cores |
| memory | 32 GB of DDR5 |
| graphics | the integrated Intel Arc; the laptop's NVIDIA RTX 500 was not used |
| system | Windows 11, KSP in a 1280×720 window, vertical sync off |

On mains power, at maximum performance. **Figures taken on another machine are not comparable to these,
not even as a difference within a run**: nothing measured here says how much of a difference would
survive a change of processor, memory or graphics. The `BENCH machine` line of every log says which
machine it was taken on.

### The logs

Twice, because a run measures one mode, and `calibrate` does real work of its own in the frames
`counters` times:

| log | mode |
|---|---|
| [`mun-05km-stock-calibrate.log`](runs/mun-05km-stock-calibrate.log) | `calibrate` |
| [`mun-05km-stock-counters.log`](runs/mun-05km-stock-counters.log) | `counters` |

Their `BENCH run` lines say they are the same flight: the same save and craft over the Mun, starting at
UT 54.86 and 54.78 at 5 000.0 m, for 149.8 and 149.9 seconds of game time against as much real time —
no warp. The craft is on rails, so loading the save covers the same ground every time, which is what
makes two runs comparable at all. The `counters` run recorded 148 samples and built **3 211 quads, of
which 1 272 of the highest subdivision level** — the ones carrying a collider.

## What a vertex costs

40 quads calibrated, 72 000 vertices per formula:

| | |
|---|---|
| `stockNsPerVertex` | 229.6 |
| `installedNsPerVertex` | 228.5 |
| `differenceNsPerVertex` | −1.1 |

**A stock terrain vertex is placed in about 230 ns.** `PQS.BuildVertexSurfaceRelative` makes five trips
into the native engine for it: `Transform.TransformPoint`, `Transform.InverseTransformPoint`, and two
reads of `Component.transform`, since it runs once per vertex and reads `base.transform` and
`buildQuad.transform` each time.

**With nothing installed, this run is the instrument measuring itself.** The two columns are the same
code reached two different ways — `installed` through a delegate on the stock method, `stock` through
the reverse-patched stub — so they have to agree. They are **1.1 ns apart, 0.5 %**, which is the floor
of the method: any difference a terrain mod's run reports carries that much of the measurement itself.

That floor is not the reproducibility of the instrument. **From one session of KSP to the next, on the
same flight, the whole replay runs a little faster or slower**: the `stock` yardstick read 229.6 ns here,
[232.3](https://github.com/lhervier/KSP-TerrainPrecisionFix-StockQuadCache/blob/master/perfs/README.md)
and [228.5](https://github.com/lhervier/KSP-TerrainPrecisionFix/blob/master/perfs/README.md) in the two
other runs of the same campaign, a 1.7 % spread, and the `installed` column moves with it. This is why a
run is read through `differenceNsPerVertex`, installed against **its own** yardstick, and never through
its `installedNsPerVertex` set against another run's: a difference taken within one session cancels
the drift, one taken across two sessions adds it.

## In flight

Every figure is a total over the samples of the `counters` run: frames over real seconds, build time
over quads built, terrain update time over frames, and over real seconds for its share.

| | |
|---|---|
| frames per second | 77.04 |
| quads built per second | 21.51 |
| of which of the highest level | 8.52 (40 %) |
| ms per quad | 1.652 |
| ms per quad of the highest level | 1.716 |
| terrain per frame | 1.195 ms |
| terrain share of real time | 9.21 % |

**Building a quad of the highest level costs about 1.7 ms**, some 7.6 µs per vertex, nearly all of it
spent in the `PQSMod`s that compute height and colour. Placing the vertex — the 230 ns above — is
**3.0 %** of that. It is worth knowing before reading any figure about a placement: a mod can make that
step three times cheaper and move the cost of a quad by two percent.

At this altitude the terrain takes about a tenth of the game's real time, which is what a mod touching
it has to be measured against. The frame rate itself says more about the integrated graphics than about
the terrain.

## Measurements read against this one

- [Stock Quad Cache](https://github.com/lhervier/KSP-TerrainPrecisionFix-StockQuadCache), stock's
  arithmetic with the two `Transform`s read once per quad.
- [Terrain Precision Fix](https://github.com/lhervier/KSP-TerrainPrecisionFix), which replaces the
  arithmetic as well.

Each of them keeps its own runs and its own reading of them; both were taken on this same save, on the
same day, on this machine.
