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

Twice, because `calibrate` does real work of its own in the frame it measures, so frame times have to
come from a separate run:

| log | mode |
|---|---|
| [`mun-05km-stock-calibrate.log`](runs/mun-05km-stock-calibrate.log) | `calibrate` |
| [`mun-05km-stock-counters.log`](runs/mun-05km-stock-counters.log) | `counters` |

Both recorded 147 samples over 150 seconds and built **2 484 quads, of which exactly 960 of the highest
subdivision level** — the ones carrying a collider. The craft is on rails, so loading the save covers
the same ground every time, which is what makes two runs comparable at all.

## What a vertex costs

30 quads calibrated, 54 000 vertices per formula:

| | |
|---|---|
| `stockNsPerVertex` | 283.0 |
| `installedNsPerVertex` | 285.2 |
| `differingQuads` | 0 / 30 |

**A stock terrain vertex is placed in about 285 ns.** `PQS.BuildVertexSurfaceRelative` makes five trips
into the native engine for it: `Transform.TransformPoint`, `Transform.InverseTransformPoint`, and two
reads of `Component.transform`, since it runs once per vertex and reads `base.transform` and
`buildQuad.transform` each time.

**With nothing installed, this run is the instrument measuring itself.** The two columns are the same
code reached two different ways — `installed` through a delegate on the stock method, `stock` through
the reverse-patched stub — so they have to agree. They are **2.2 ns apart, 0.8 %**, which is the floor
of the method and of the same order as its reproducibility between two sessions. Any run of a terrain
mod should be read knowing that 2.2 ns of what it reports is the measurement itself.

## In flight

| | |
|---|---|
| frames per second | 114.72 |
| quads built per second | 16.56 |
| of which of the highest level | 6.40 (39 %) |
| ms per quad | 2.691 |
| ms per quad of the highest level | 2.771 |
| terrain per frame | 0.947 ms |
| terrain share of real time | 10.86 % |

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
