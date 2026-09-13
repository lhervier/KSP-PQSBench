using System.Diagnostics;
using HarmonyLib;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>
    /// Times one terrain quad being built. PQS.BuildQuad is the loop over the vertices of a single quad, so
    /// it runs once per quad actually built, and what it costs includes whatever a mod has put in the way
    /// of the vertex placement.
    /// </summary>
    [HarmonyPatch(typeof(PQS), "BuildQuad")]
    internal static class BuildQuadPatch
    {
        private static void Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void Postfix(PQS __instance, PQ quad, bool __result, long __state)
        {
            // A false result is a call that returned without building anything, which is what the
            // calibration's own re-entry gets. IsReplaying covers the rest.
            if (!__result || quad == null || Calibration.IsReplaying)
            {
                return;
            }
            PQSBenchMod.OnQuadBuilt(__instance, quad, Stopwatch.GetTimestamp() - __state);
        }
    }

    /// <summary>
    /// Times the whole terrain update of one sphere for one frame, which contains the quad builds above
    /// along with the subdivision decisions and the normals. This is what a frame pays.
    /// </summary>
    [HarmonyPatch(typeof(PQS), "UpdateQuads")]
    internal static class UpdateQuadsPatch
    {
        private static void Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void Postfix(long __state)
        {
            Counters.RecordUpdate(Stopwatch.GetTimestamp() - __state);
        }
    }
}
