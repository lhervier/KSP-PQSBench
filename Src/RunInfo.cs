using System;
using System.Diagnostics;
using System.Globalization;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>
    /// Which run a log is: the save and the craft it was flown on, the stretch of game time it covers, and
    /// how the terrain was set up. Two runs that agree on all of it flew over the same ground, which is
    /// what comparing their figures rests on.
    ///
    /// Written the same way whatever is being measured, so that the two modes produce logs that can be
    /// paired up.
    /// </summary>
    internal static class RunInfo
    {
        private static bool _started;
        private static double _utStart;
        private static long _startTicks;
        private static double _altitudeStart;

        /// <summary>
        /// Notes the run as having started, unless it already has. Called on every frame: a run starts on
        /// the first one spent in flight since the last reset, whichever mode is measuring.
        /// </summary>
        public static void NoteFrame()
        {
            if (_started || !HighLogic.LoadedSceneIsFlight)
            {
                return;
            }
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                return;
            }
            _utStart = Planetarium.GetUniversalTime();
            _startTicks = Stopwatch.GetTimestamp();
            _altitudeStart = vessel.altitude;
            _started = true;
        }

        /// <summary>
        /// Writes the machine the run was taken on, which run this is, and how the terrain it flew over was
        /// set up.
        /// </summary>
        public static void Dump()
        {
            // Written even when no run started: figures from two machines are never comparable, whatever
            // else their logs agree on. The memory type is not in it, Unity does not expose it. The graphics
            // device is, since a laptop can run KSP on either of its two and the frame rate follows.
            Log.Info($"BENCH machine;cpu={UnityEngine.SystemInfo.processorType}"
                + $";logicalCores={FormatUtils.I(UnityEngine.SystemInfo.processorCount)}"
                + $";memoryMb={FormatUtils.I(UnityEngine.SystemInfo.systemMemorySize)}"
                + $";gpu={UnityEngine.SystemInfo.graphicsDeviceName}"
                + $";os={UnityEngine.SystemInfo.operatingSystem}");

            if (!_started)
            {
                Log.Warning("BENCH run;the flight scene was never reached since the last reset:"
                    + " what follows belongs to no run");
                return;
            }

            Vessel vessel = FlightGlobals.ActiveVessel;
            CelestialBody body = vessel == null ? null : vessel.mainBody;
            double ut = Planetarium.fetch == null ? _utStart : Planetarium.GetUniversalTime();
            double realSeconds = (Stopwatch.GetTimestamp() - _startTicks) / (double)Stopwatch.Frequency;

            // What the player asked of the terrain, which is what decides how far a sphere subdivides.
            PQSCache.PQSGlobalPresetList presets = PQSCache.PresetList;
            string presetName = presets == null ? "unknown" : presets.preset;
            string presetIndex = presets == null ? "unknown" : FormatUtils.I(presets.presetIndex);

            // Game time against real time is also what says whether the run warped: at normal rate the
            // two stay within a few per cent of each other.
            Log.Info($"BENCH run;save={(HighLogic.CurrentGame == null ? "none" : HighLogic.CurrentGame.Title)}"
                + $";vessel={(vessel == null ? "none" : vessel.vesselName)}"
                + $";body={(body == null ? "none" : body.bodyName)}"
                + $";terrainPreset={presetName};terrainPresetIndex={presetIndex}"
                + $";verticesPerQuad={PQS.cacheVertCount}"
                + $";utStart={FormatUtils.F(_utStart, 2)};utEnd={FormatUtils.F(ut, 2)};utSpan={FormatUtils.F(ut - _utStart, 2)}"
                + $";realSeconds={FormatUtils.F(realSeconds, 2)}"
                + $";altitudeStart={FormatUtils.F(_altitudeStart, 1)}"
                + $";altitude={FormatUtils.F(vessel == null ? 0.0 : vessel.altitude, 1)}"
                + $";speed={FormatUtils.F(vessel == null ? 0.0 : vessel.srfSpeed, 1)}");

            // Only the sphere the craft is flying over: it is the one whose quads were measured. A body
            // with an ocean has a second sphere, which builds quads too and is not described here.
            if (body != null && body.pqsController != null)
            {
                DescribeSphere(body.pqsController);
            }
        }

        /// <summary>
        /// Writes what decides how far a terrain sphere subdivides: the altitude the highest level appears
        /// under, which levels carry a collider, and how fast the craft may cross the ground before the
        /// game stops building that highest level at all.
        /// </summary>
        private static void DescribeSphere(PQS sphere)
        {
            int maxLevel = sphere.maxLevel;

            // A quad of level L splits when the craft is closer than subdivisionThresholds[L], so the
            // highest level appears under the threshold of the level below it.
            double highestLevelUnder = sphere.subdivisionThresholds != null
                && maxLevel - 1 >= 0 && maxLevel - 1 < sphere.subdivisionThresholds.Length
                ? sphere.subdivisionThresholds[maxLevel - 1]
                : 0.0;

            // PQS.UpdateVisual refuses to subdivide past the level whose quads are wider than what the
            // craft crosses between two samples, which is a real time interval: the slower the game runs,
            // the lower this ceiling falls.
            double angleCap = 1.5707963 / Math.Pow(2.0, maxLevel) * sphere.maxQuadLenghtsPerFrame;
            Log.Info($"BENCH sphere;name={sphere.name};radius={FormatUtils.F(sphere.radius, 0)}"
                + $";minLevel={sphere.minLevel};maxLevel={maxLevel}"
                + $";highestLevelUnder={FormatUtils.F(highestLevelUnder, 0)}"
                + $";subdivisionOffAbove={FormatUtils.F(sphere.maxDetailDistance * sphere.radius, 0)}"
                + $";maxAnglePerSample={angleCap.ToString("E3", CultureInfo.InvariantCulture)}"
                + $";speedCapAt60Fps={FormatUtils.F(angleCap * sphere.radius * 60.0, 0)}");

            PQSMod_QuadMeshColliders[] colliders =
                sphere.GetComponentsInChildren<PQSMod_QuadMeshColliders>(true);
            if (colliders == null || colliders.Length == 0)
            {
                Log.Info($"BENCH colliders;name={sphere.name};none");
                return;
            }
            foreach (PQSMod_QuadMeshColliders collider in colliders)
            {
                Log.Info($"BENCH colliders;name={sphere.name};maxLevelOffset={collider.maxLevelOffset}"
                    + $";lowestLevelWithCollider={maxLevel - Math.Abs(collider.maxLevelOffset)}");
            }
        }

        /// <summary>Forgets the run, so that the next one starts on the next frame in flight.</summary>
        public static void Reset()
        {
            _started = false;
        }
    }
}
