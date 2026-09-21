namespace com.github.lhervier.ksp.pqsbench.bench.calibrate
{
    /// <summary>Every constant of the calibrate bench.</summary>
    internal static class CalibrateConstants
    {
        // The Harmony instance that reverse patches the stock placement. It patches nothing the game runs.
        internal const string HarmonyId = "com.github.lhervier.ksp.pqsbench.calibrate";

        // How many times each formula is replayed over a quad, and how often a quad is used for it. One
        // pass over a couple of hundred vertices already lasts far longer than the timer's resolution; the
        // repeats are there to average, not to make the measurement possible. Each of them starts on a quad
        // the formula has not seen, so a formula that keeps something per quad pays for it once per round,
        // the way a real build pays it once per quad.
        internal const int Rounds = 8;
        internal const int OneQuadIn = 32;

        // The three things timed, all reached the same way and replayed by the same loop.
        //   installed - the stock method itself, so it runs through whatever Harmony patch is on it today,
        //               or straight to stock when there is none. Nothing here knows which;
        //   stock     - the stock placement, reached through a reverse patch of the stock method, so it
        //               stays measurable in a run where that method is patched. It is the yardstick each
        //               run is read against;
        //   harness   - places nothing. What is left is what the replay itself costs, the fields written
        //               before each call and the indirect call, which the two others also pay.
        internal const int FormulaHarness = 0;
        internal const int FormulaStock = 1;
        internal const int FormulaInstalled = 2;
        internal const int FormulaCount = 3;
    }
}
