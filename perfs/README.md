# What the stock terrain costs

A reference run of stock KSP, with no mod touching the terrain at all: what building a quad costs, what
placing a vertex costs, and what a frame pays for it. It is the yardstick any measurement of a terrain
mod is read against, and it is here because it says nothing about any mod — only about the game.

The procedure that produced it is on [this mod's page](../README.md): the craft, the orbit, how long to
fly, and what makes a run worth keeping. What follows is what it produced.

## The run

2026-09-14, KSP 1.12.5. `GameData` holding Harmony, ModuleManager, KSP Community Fixes and this
measuring mod — **nothing patching the terrain**. A command pod on rails in a circular orbit 5 km over
the Mun, flown for 150 seconds of game time.

Twice, because a run measures one mode, and `calibrate` does real work of its own in the frames
`counters` times:

| log | mode |
|---|---|
| [`mun-05km-stock-calibrate.log`](runs/mun-05km-stock-calibrate.log) | `calibrate` |
| [`mun-05km-stock-counters.log`](runs/mun-05km-stock-counters.log) | `counters` |

Their `BENCH run` lines say they are the same flight: the same save and craft over the Mun, starting at
UT 335.56 at 4 999.8 m, for 149.9 and 151.1 seconds of game time against as much real time — no warp.
The craft is on rails, so loading the save covers the same ground every time, which is what makes two
runs comparable at all. The `counters` run recorded 148 samples and built **2 492 quads, of which 968 of
the highest subdivision level** — the ones carrying a collider.

## What a vertex costs

30 quads calibrated, 54 000 vertices per formula:

| | |
|---|---|
| `stockNsPerVertex` | 284.7 |
| `installedNsPerVertex` | 285.9 |
| `differenceNsPerVertex` | +1.2 |
| `differingQuads` | 0 / 30 |

**A stock terrain vertex is placed in about 285 ns.** `PQS.BuildVertexSurfaceRelative` makes five trips
into the native engine for it: `Transform.TransformPoint`, `Transform.InverseTransformPoint`, and two
reads of `Component.transform`, since it runs once per vertex and reads `base.transform` and
`buildQuad.transform` each time.

**With nothing installed, this run is the instrument measuring itself.** The two columns are the same
code reached two different ways — `installed` through a delegate on the stock method, `stock` through
the reverse-patched stub — so they have to agree. They are **1.2 ns apart, 0.4 %**, which is the floor
of the method: any difference a terrain mod's run reports carries that much of the measurement itself.

That floor is not the reproducibility of the instrument. **From one session of KSP to the next, on the
same flight, the whole replay runs a little faster or slower**: the `stock` yardstick read 284.7 ns here,
[290.3](https://github.com/lhervier/KSP-TerrainPrecisionFix-StockQuadCache/blob/main/perfs/README.md)
and [288.0](https://github.com/lhervier/KSP-TerrainPrecisionFix/blob/main/perfs/README.md) in the two
other runs of the same campaign, a 2 % spread, and the `installed` column moves with it. This is why a
run is read through `differenceNsPerVertex`, installed against **its own** yardstick, and never through
its `installedNsPerVertex` set against another run's: a difference taken within one session cancels
the drift, one taken across two sessions adds it.

## In flight

Every figure is a total over the samples of the `counters` run: frames over real seconds, build time
over quads built, terrain update time over frames, and over real seconds for its share.

| | |
|---|---|
| frames per second | 114.56 |
| quads built per second | 16.49 |
| of which of the highest level | 6.41 (39 %) |
| ms per quad | 2.693 |
| ms per quad of the highest level | 2.775 |
| terrain per frame | 0.937 ms |
| terrain share of real time | 10.74 % |

**Building a quad costs about 2.8 ms**, some 12 µs per vertex, nearly all of it spent in the `PQSMod`s
that compute height and colour. Placing the vertex — the 285 ns above — is **2.3 %** of that. It is
worth knowing before reading any figure about a placement: a mod can make that step three times cheaper
and move the cost of a quad by one and a half percent.

At this altitude the terrain takes about a tenth of the game's real time, which is what a mod touching
it has to be measured against.

## Measurements read against this one

- [Stock Quad Cache](https://github.com/lhervier/KSP-TerrainPrecisionFix-StockQuadCache), stock's
  arithmetic with the two `Transform`s read once per quad.
- [Terrain Precision Fix](https://github.com/lhervier/KSP-TerrainPrecisionFix), which replaces the
  arithmetic as well.

Each of them keeps its own runs and its own reading of them; both were taken on this same save, on the
same day, on this machine.
