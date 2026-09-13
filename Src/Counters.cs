using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>
    /// What the terrain costs in flight: how many quads the game builds, how long each one takes, and how
    /// much of a frame that is, cut into samples of one second of game time.
    ///
    /// Everything is accumulated in memory, in arrays allocated once, and nothing is written until it is
    /// asked for: writing to KSP.log while measuring would cost more than what is being measured.
    /// </summary>
    internal static class Counters
    {
        // One sample per second of game time, so that two runs of the same flight are cut into pieces
        // covering the same stretch of trajectory even if one of them runs slower than the other.
        private const double SampleSeconds = 1.0;
        private const int MaxSamples = 4096;

        private static readonly Sample[] _samples = new Sample[MaxSamples];
        private static int _sampleCount;
        private static bool _full;

        private static Sample _current;
        private static bool _open;
        private static long _currentStartTicks;
        private static int _warpedSeconds;

        // Terrain spheres whose subdivision settings have been logged, so that they are logged once.
        private static readonly HashSet<PQS> _describedSpheres = new HashSet<PQS>();

        /// <summary>How many samples have been recorded so far.</summary>
        public static int SampleCount { get { return _sampleCount; } }

        /// <summary>How many seconds of game time were dropped because the game was warping.</summary>
        public static int WarpedSeconds { get { return _warpedSeconds; } }

        /// <summary>Counts a frame, and closes the current sample once a second of game time has passed.</summary>
        public static void OnFrame()
        {
            if (_full || !HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            // Time warp is not measured at all: the craft crosses the ground far too fast for a sample to
            // mean anything, and a sample per second of game time would be hundreds of samples per second.
            if (TimeWarp.CurrentRate > 1.05f)
            {
                if (_open)
                {
                    _open = false;
                    _warpedSeconds++;
                }
                return;
            }

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                return;
            }
            double ut = Planetarium.GetUniversalTime();

            if (!_open)
            {
                Open(ut);
                return;
            }

            _current.Frames++;
            if (ut - _current.Ut < SampleSeconds)
            {
                return;
            }

            // Where the craft was is read at the end of the sample rather than averaged over it: across a
            // second of a ballistic pass it barely moves, and its only job is to say where this was taken.
            _current.UtSpan = ut - _current.Ut;
            _current.RealSeconds = (Stopwatch.GetTimestamp() - _currentStartTicks) / (double)Stopwatch.Frequency;
            _current.Altitude = vessel.altitude;
            _current.Speed = vessel.srfSpeed;
            Close();
            Open(ut);
        }

        private static void Open(double ut)
        {
            _current = new Sample();
            _current.Ut = ut;
            _current.SpeedLevelCap = int.MaxValue;

            // The frame a sample opens on is counted here, since the clock starts on it too. The frame two
            // samples straddle therefore counts in both, which is what it costs: it spans both.
            _current.Frames = 1;
            _currentStartTicks = Stopwatch.GetTimestamp();
            _open = true;
        }

        private static void Close()
        {
            if (_sampleCount >= MaxSamples)
            {
                if (!_full)
                {
                    _full = true;
                    Log.Warning(MaxSamples + " samples recorded, no more room: dump them (Alt+F8) and"
                        + " start again (Alt+F7)");
                }
                _open = false;
                return;
            }
            _samples[_sampleCount++] = _current;
        }

        /// <summary>Records one terrain quad actually built, and the time it took.</summary>
        public static void RecordQuad(PQS sphere, PQ quad, long ticks, bool topLevel)
        {
            if (_describedSpheres.Add(sphere))
            {
                DescribeSphere(sphere);
            }
            if (!_open)
            {
                return;
            }

            _current.Quads++;
            _current.Vertices += PQS.cacheVertCount;
            _current.BuildTicks += ticks;
            _current.SubdivisionSum += quad.subdivision;
            if (quad.subdivision > _current.SubdivisionMax)
            {
                _current.SubdivisionMax = quad.subdivision;
            }
            if (topLevel)
            {
                _current.TopLevelQuads++;
                _current.TopLevelBuildTicks += ticks;
            }

            // The lowest ceiling the sphere put on subdivision while this sample lasted. Below maxLevel,
            // the craft is crossing the ground too fast for the game to build the quads that carry a
            // collider.
            if (sphere.maxLevelAtCurrentTgtSpeed < _current.SpeedLevelCap)
            {
                _current.SpeedLevelCap = sphere.maxLevelAtCurrentTgtSpeed;
            }
            _current.MaxLevel = sphere.maxLevel;
        }

        /// <summary>Records what one terrain update of one sphere cost, for one frame.</summary>
        public static void RecordUpdate(long ticks)
        {
            if (!_open)
            {
                return;
            }
            _current.UpdateTicks += ticks;
        }

        /// <summary>
        /// Logs, once per terrain sphere, what decides how far it subdivides: the altitude the highest
        /// level appears under, which levels carry a collider, and how fast the craft may cross the ground
        /// before the game stops building that highest level at all.
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
            Log.Info($"BENCH sphere;name={sphere.name};radius={Fmt.F(sphere.radius, 0)}"
                + $";minLevel={sphere.minLevel};maxLevel={maxLevel}"
                + $";highestLevelUnder={Fmt.F(highestLevelUnder, 0)}"
                + $";subdivisionOffAbove={Fmt.F(sphere.maxDetailDistance * sphere.radius, 0)}"
                + $";maxAnglePerSample={angleCap.ToString("E3", CultureInfo.InvariantCulture)}"
                + $";speedCapAt60Fps={Fmt.F(angleCap * sphere.radius * 60.0, 0)}");

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

        /// <summary>Writes one line per sample recorded, as semicolon separated values.</summary>
        public static void Dump()
        {
            Log.Info("BENCH;sample;ut;utSpan;realSeconds;frames;fps;altitude;speed;quads;topLevelQuads"
                + ";vertices;buildMs;topLevelBuildMs;updateMs;subdivisionAvg;subdivisionMax;speedLevelCap;maxLevel");
            for (int i = 0; i < _sampleCount; i++)
            {
                Sample s = _samples[i];
                double buildMs = s.BuildTicks * 1000.0 / Stopwatch.Frequency;
                double topLevelBuildMs = s.TopLevelBuildTicks * 1000.0 / Stopwatch.Frequency;
                double updateMs = s.UpdateTicks * 1000.0 / Stopwatch.Frequency;
                double fps = s.RealSeconds > 0.0 ? s.Frames / s.RealSeconds : 0.0;
                double subdivisionAvg = s.Quads > 0 ? s.SubdivisionSum / (double)s.Quads : 0.0;
                Log.Info(string.Join(";", new[]
                {
                    "BENCH",
                    Fmt.I(i),
                    Fmt.F(s.Ut, 2), Fmt.F(s.UtSpan, 3), Fmt.F(s.RealSeconds, 3),
                    Fmt.I(s.Frames), Fmt.F(fps, 1),
                    Fmt.F(s.Altitude, 1), Fmt.F(s.Speed, 1),
                    Fmt.I(s.Quads), Fmt.I(s.TopLevelQuads), Fmt.L(s.Vertices),
                    Fmt.F(buildMs, 3), Fmt.F(topLevelBuildMs, 3), Fmt.F(updateMs, 3),
                    Fmt.F(subdivisionAvg, 2), Fmt.I(s.SubdivisionMax),
                    Fmt.I(s.SpeedLevelCap == int.MaxValue ? -1 : s.SpeedLevelCap), Fmt.I(s.MaxLevel)
                }));
            }
        }

        /// <summary>Throws away everything recorded, to start another run without restarting KSP.</summary>
        public static void Reset()
        {
            _sampleCount = 0;
            _full = false;
            _open = false;
            _warpedSeconds = 0;
            _describedSpheres.Clear();
        }
    }
}
