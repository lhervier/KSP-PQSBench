# What the stock terrain costs: the runs

The logs of the reference runs of stock KSP, with no mod touching the terrain at all. The figures
themselves, and what they say, are in [What stock costs](../README.md#what-stock-costs); the procedure
that produced them is on [the same page](../README.md#measuring-a-terrain-mod).

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
the same number of quads of the highest level says the two flights built the same ground. Their result
lines, as logged:

```
BENCH calibration;quads=22;roundsPerQuad=8;verticesPerFormula=39600;stockNsPerVertex=284.2;installedNsPerVertex=281.7;differenceNsPerVertex=-2.5;harnessNsPerVertex=5.7;stockRawNsPerVertex=289.9;installedRawNsPerVertex=287.4
BENCH calibration;quads=22;roundsPerQuad=8;verticesPerFormula=39600;stockNsPerVertex=284.7;installedNsPerVertex=288.2;differenceNsPerVertex=3.4;harnessNsPerVertex=5.9;stockRawNsPerVertex=290.7;installedRawNsPerVertex=294.1
```

## Runs read against these

- [Stock Quad Cache](https://github.com/lhervier/KSP-TerrainPrecisionFix-StockQuadCache/blob/master/perfs/README.md),
  stock's arithmetic with the two `Transform`s read once per quad.
- [Terrain Precision Fix](https://github.com/lhervier/KSP-TerrainPrecisionFix/blob/master/perfs/README.md),
  which replaces the arithmetic as well.

Each of them keeps its own runs; all were taken on this same save, in the same session of runs, on this
machine.
