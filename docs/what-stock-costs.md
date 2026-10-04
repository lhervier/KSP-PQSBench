# What stock costs

Part of [PQS Bench](../README.md). The short version is on the main page, under
[What stock costs](../README.md#what-stock-costs); here are the two stock runs, and what they say about
the instrument itself.

Two runs of KSP 1.12.5 with nothing patching the terrain, flown from the provided save by the procedure
of [Measuring a terrain mod](measuring-a-terrain-mod.md#the-runs), recording from 30 s to 1 min 40 s of
mission time. **Taken on my desktop**: Intel Core i7-4790K, 32 GB of DDR3, NVIDIA GeForce GTX 1060,
Windows 10. Figures from another machine are not comparable to these. The logs, and the machine in full,
are in [`perfs/`](../perfs/README.md).

22 quads calibrated per run, 39 600 vertices per formula:

| | run 1 | run 2 |
|---|---|---|
| `stockNsPerVertex` | 284.2 ns | 284.7 ns |
| `installedNsPerVertex` | 281.7 ns | 288.2 ns |
| `differenceNsPerVertex` | −2.5 ns | +3.4 ns |
| `harnessNsPerVertex` | 5.7 ns | 5.9 ns |

**A stock terrain vertex is placed in about 285 ns.** `PQS.BuildVertexSurfaceRelative` makes five trips
into the native engine for it: `Transform.TransformPoint`, `Transform.InverseTransformPoint`, and two
reads of `Component.transform`, since it runs once per vertex and reads `base.transform` and
`buildQuad.transform` each time.

**With nothing installed, these runs are the instrument measuring itself.** The two columns are the same
code reached two different ways — `installed` through a delegate on the stock method, `stock` through
the reverse-patched stub — so they have to agree. They are 2.5 ns apart one way, then 3.4 ns the other:
**about 3 ns, 1 %, is the floor of the method**, and the change of sign says it is noise rather than a
bias. Any difference a terrain mod's run reports carries that much of the measurement itself.

That floor is not the reproducibility of the instrument. From one session of KSP to the next, on the same
flight, the whole replay runs a little faster or slower: over these two runs and the four of the mods
measured against them in the same session, the `stock` yardstick read between 282.7 and 297.5 ns, a 5 %
spread, and the `installed` column moves with it. This is why a run is read through
`differenceNsPerVertex`, within one run, and never through an `installedNsPerVertex` set against
another run's.
