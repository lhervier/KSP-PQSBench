# What the stock terrain costs

Reference runs of stock KSP, with no mod touching the terrain at all: what placing a terrain vertex
costs. They are the yardstick any measurement of a terrain mod is read against, and they are here because
they say nothing about any mod — only about the game.

The procedure that produced them is on [this mod's page](../README.md): the craft, the orbit, how long to
fly, and what makes a run worth keeping. What follows is what it produced.

## The runs

KSP 1.12.5. `GameData` holding Harmony, ModuleManager, KSP Community Fixes 1.41.1 and this measuring mod
— **nothing patching the terrain**. A command pod on rails in a circular orbit 5 km over the Mun, from the
save kept next to this page: [`ref-mune-5km.sfs`](ref-mune-5km.sfs). Recording started at 30 s of mission
time (Alt+F7) and was dumped at 1 min 40 s (Alt+F8): 70 seconds of game time.

### The machine

| | |
|---|---|
| processor | Intel Core i7-4790K, 4 cores, 8 logical cores |
| memory | 32 GB of DDR3 |
| graphics | NVIDIA GeForce GTX 1060 6 GB |
| system | Windows 10, *High performance* power plan |
| KSP | a 1280×720 window, vertical sync off, no frame limit, terrain detail *High* |

**Figures taken on another machine are not comparable to these, not even as a difference within a run**:
nothing measured here says how much of a difference would survive a change of processor, memory or
graphics. The `BENCH machine` line of every log says which machine it was taken on.

### The logs

Two runs, each in a fresh KSP. They were interleaved with the runs of the two other configurations read
against them (stock, then Stock Quad Cache, then Terrain Precision Fix, twice over), so that no
configuration had all its runs at the same moment of the session.

| log | starts at | game time | real time | quads of the highest level built |
|---|---|---|---|---|
| [`mun-05km-stock-calibrate-1.log`](runs/mun-05km-stock-calibrate-1.log) | UT 54.92 | 69.72 s | 69.82 s | 704 |
| [`mun-05km-stock-calibrate-2.log`](runs/mun-05km-stock-calibrate-2.log) | UT 54.70 | 70.00 s | 70.13 s | 704 |

The figures come from their `BENCH run` lines. Game time against real time says there was no warp, and
the same number of quads of the highest level says the two flights built the same ground.

## What a vertex costs

22 quads calibrated per run, 39 600 vertices per formula:

| | run 1 | run 2 |
|---|---|---|
| `stockNsPerVertex` | 284.2 | 284.7 |
| `installedNsPerVertex` | 281.7 | 288.2 |
| `differenceNsPerVertex` | −2.5 | +3.4 |
| `harnessNsPerVertex` | 5.7 | 5.9 |

**A stock terrain vertex is placed in about 285 ns.** `PQS.BuildVertexSurfaceRelative` makes five trips
into the native engine for it: `Transform.TransformPoint`, `Transform.InverseTransformPoint`, and two
reads of `Component.transform`, since it runs once per vertex and reads `base.transform` and
`buildQuad.transform` each time.

**With nothing installed, these runs are the instrument measuring itself.** The two columns are the same
code reached two different ways — `installed` through a delegate on the stock method, `stock` through
the reverse-patched stub — so they have to agree. They are 2.5 ns apart one way, then 3.4 ns the other:
**about 3 ns, 1 %, is the floor of the method**, and the change of sign says it is noise rather than a
bias. Any difference a terrain mod's run reports carries that much of the measurement itself.

That floor is not the reproducibility of the instrument. **From one session of KSP to the next, on the
same flight, the whole replay runs a little faster or slower**: over the six runs of the campaign, the
`stock` yardstick read between 282.7 and 297.5 ns, a 5 % spread, and the `installed` column moves with
it. This is why a run is read through `differenceNsPerVertex`, installed against **its own** yardstick,
and never through its `installedNsPerVertex` set against another run's: a difference taken within one
session cancels the drift, one taken across two sessions adds it.

## Measurements read against this one

- [Stock Quad Cache](https://github.com/lhervier/KSP-TerrainPrecisionFix-StockQuadCache/blob/master/perfs/README.md),
  stock's arithmetic with the two `Transform`s read once per quad.
- [Terrain Precision Fix](https://github.com/lhervier/KSP-TerrainPrecisionFix/blob/master/perfs/README.md),
  which replaces the arithmetic as well.

Each of them keeps its own runs and its own reading of them; all were taken on this same save, in the
same session of runs, on this machine.
