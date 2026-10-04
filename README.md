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

Everything the bench records comes from one Harmony hook on `PQS.BuildQuad`: **a quad is built**. In
flight, a small window holds two buttons, *Reset* and *Dump to KSP.log*, and `Alt+F6` shows or hides it.
Nothing is written until *Dump*, and the dump says for itself what it measured: which mods patch the
terrain, the machine, the stretch flown, and how the terrain of that body is set up.

**→ Full chapter: [How the bench works](docs/how-the-bench-works.md)**

## What is installed, against stock, on the same quads

The bench answers one question: what does placing **one terrain vertex** cost, and how much of that does
the installed mod change? It replays the placement on quads of the highest subdivision level, in the frame
that built them: whatever is installed, stock, and the harness alone, eight rounds each, in turn. **Read
`differenceNsPerVertex` within one run.** A mod that keeps state per terrain quad must invalidate it on a
`PQS.BuildQuad` prefix.

**→ Full chapter: [What is installed, against stock, on the same quads](docs/what-is-installed-against-stock.md)**

## Measuring a terrain mod

A run with the mod being measured installed, against a run with its folder taken out of `GameData`, both
flown from the same save on rails: a command pod 5 km over the Mun, provided in [`perfs/`](perfs/). Each
run is a fresh KSP, vertical sync off, the camera turned the same way, *Reset* and *Dump to KSP.log* at the
same two mission times; the runs can be played by hand, or by a script through KSP-MCPServer.

**→ Full chapter: [Measuring a terrain mod](docs/measuring-a-terrain-mod.md)**

## What stock costs

Two runs of KSP 1.12.5 with nothing patching the terrain, flown by that protocol on my desktop (figures
from another machine are not comparable): **a stock terrain vertex is placed in about 285 ns.** With
nothing installed, the `installed` and `stock` columns are the same code reached two ways, and they agree
within **about 3 ns, 1 %: the floor of the method**. From one session of KSP to the next, the whole replay
runs a little faster or slower, its `stock` yardstick over a 5 % spread: a run is read through
`differenceNsPerVertex`, within one run.

**→ Full chapter: [What stock costs](docs/what-stock-costs.md)**

## Install

Requires KSP 1.12 and [HarmonyKSP](https://github.com/KSPModdingLibs/HarmonyKSP).

Copy `GameData/PQSBenchMod` into the `GameData` of KSP. Nothing is written to your saves. **It measures
as soon as it is installed**, and its replay costs time in the frames that build terrain: take it out of
`GameData` when you are not measuring.

## Settings

`GameData/PQSBenchMod/PluginData/settings.cfg`, read once when KSP starts. To change it: quit KSP, edit
the file, start KSP again. It holds a single setting.

`logLevel` takes `Error`, `Warning`, `Info` (default), `Debug` or `Trace`. **Leave it at `Info`**: the
results are written at `Info`, so `Warning` or `Error` would silence them, and nothing in the mod writes
above `Info`.

## Build

Set `KSPDIR` to your KSP install folder, which must contain `GameData/000_Harmony`, and run `build.bat`.
It needs the .NET SDK, and produces `GameData/PQSBenchMod/PQSBenchMod.dll`.

## Licence

MIT, see [LICENSE](LICENSE).
