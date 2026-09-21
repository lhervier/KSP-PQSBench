using System.Diagnostics;
using HarmonyLib;

namespace com.github.lhervier.ksp.pqsbench.events
{
    /// <summary>Receives one terrain quad the game has just built.</summary>
    /// <param name="sphere">The terrain sphere that built it.</param>
    /// <param name="quad">The quad, fully built.</param>
    /// <param name="ticks">How long PQS.BuildQuad took, in Stopwatch ticks.</param>
    /// <param name="topLevel">
    /// Whether the quad is one of the highest subdivision level: the ones the game detaches into
    /// LocalSpacePQStorage, which carry the collider craft stand on as long as the body's
    /// PQSMod_QuadMeshColliders.maxLevelOffset is 0, and the only ones Terrain Precision Fix corrects.
    /// </param>
    internal delegate void QuadBuiltHandler(PQS sphere, PQ quad, long ticks, bool topLevel);

    /// <summary>
    /// Times one terrain quad being built, and raises Built with it. PQS.BuildQuad is the loop over the
    /// vertices of a single quad, so it runs once per quad actually built, and what it costs includes
    /// whatever a mod has put in the way of the vertex placement.
    /// </summary>
    [HarmonyPatch(typeof(PQS), "BuildQuad")]
    internal static class BuildQuadPatch
    {
        /// <summary>
        /// Raised once per quad actually built, never for a call that built nothing. A listener that makes
        /// the game build quads itself hears of those too.
        /// </summary>
        public static event QuadBuiltHandler Built;

        private static void Prefix(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void Postfix(PQS __instance, PQ quad, bool __result, long __state)
        {
            // Read first, so that neither the checks below nor the listeners are charged to the build.
            long ticks = Stopwatch.GetTimestamp() - __state;

            // A false result is a call that returned without building anything.
            if (!__result || quad == null)
            {
                return;
            }
            Built?.Invoke(__instance, quad, ticks, IsTopLevelQuad(quad));
        }

        /// <summary>
        /// Whether this quad is one of the highest subdivision level, as QuadBuiltHandler's topLevel
        /// describes it.
        /// </summary>
        private static bool IsTopLevelQuad(PQ quad)
        {
            PQS sphere = quad.sphereRoot;

            // Stock has two ways of placing vertices, and only the surface relative one detaches quads
            // this way.
            if (sphere == null || !sphere.surfaceRelativeQuads || sphere.LocalSpacePQStorage == null)
            {
                return false;
            }
            return quad.transform.parent == sphere.LocalSpacePQStorage.transform;
        }
    }
}
