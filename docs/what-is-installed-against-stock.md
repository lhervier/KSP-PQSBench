# What is installed, against stock, on the same quads

Part of [PQS Bench](../README.md). The short version is on the main page, under
[What is installed, against stock, on the same quads](../README.md#what-is-installed-against-stock-on-the-same-quads);
here are why and how the vertex placement is replayed, and the line it comes out as.

The bench answers one question: what does placing **one terrain vertex** cost, and how much of that does
the installed mod change?

## Why the placement is replayed

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

## What is replayed

| | |
|---|---|
| `installed` | `PQS.BuildVertexSurfaceRelative` itself, so it runs through whatever Harmony patch is on it — or straight to stock when there is none. **The bench does not know, and does not need to know, which mod that is** |
| `stock` | the stock placement, reached through a Harmony reverse patch, which copies the original method's IL into a stub. It therefore stays measurable in a run where the stock method is patched, without being a transcription that could drift from the game — or be faster than it, which a transcription of those four lines is: stock keeps its intermediate values in fields of `PQS` and reads its inputs through a field, where a copy would use locals. It is the yardstick each run is read against |
| `harness` | places nothing. What it measures is **the fixed cost of the replay itself** — the fields written before each call, and the indirect call — which the two others also pay, and which is subtracted from both |

## Two things the replay has to reproduce

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

## What comes out

One `BENCH calibration` line, over the quads calibrated since the last *Reset*:

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
