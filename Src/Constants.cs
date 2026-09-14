using UnityEngine;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>Every constant of the mod, grouped by what uses it.</summary>
    internal static class Constants
    {
        // ---- The mod ----

        internal const string HarmonyId = "com.github.lhervier.ksp.pqsbench";

        // ---- Settings ----

        // PluginData/settings.cfg, next to the DLL. Read once, when KSP starts.
        internal const string SettingsFolder = "PluginData";
        internal const string SettingsFile = "settings.cfg";
        internal const string BenchModeSetting = "benchMode";
        internal const string LogLevelSetting = "logLevel";

        // ---- Log ----

        internal const string LogPrefix = "[PQSBench] ";

        // ---- Keys, pressed along with the modifier (Alt) ----

        internal const KeyCode DumpKey = KeyCode.F8;
        internal const KeyCode ResetKey = KeyCode.F7;

        // ---- CountersBench ----

        // One sample per second of game time, so that two runs of the same flight are cut into pieces
        // covering the same stretch of trajectory even if one of them runs slower than the other.
        internal const double SampleSeconds = 1.0;
        internal const int MaxSamples = 4096;

        // The time warp rate above which nothing is recorded. Slightly above 1, so that the rate reported at
        // normal speed is never taken for a warp.
        internal const float MaxUnwarpedRate = 1.05f;

        // ---- CalibrateBench ----

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
