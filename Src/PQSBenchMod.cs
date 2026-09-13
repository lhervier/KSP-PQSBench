using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>What the measurement records, read from settings.cfg.</summary>
    internal enum BenchMode
    {
        /// <summary>Nothing at all: no patch is installed and the mod costs nothing.</summary>
        Off,

        /// <summary>What the terrain costs in flight, one line per second of game time.</summary>
        Counters,

        /// <summary>
        /// Adds, on a sample of quads, whatever is patching the vertex placement timed against a copy of
        /// the stock one.
        /// </summary>
        Calibrate
    }

    /// <summary>
    /// Measures what building the terrain costs: how many quads the game builds while flying, how long
    /// each one takes, and how much of a frame that is.
    ///
    /// Everything is accumulated in memory, in arrays allocated once, and nothing is written until it is
    /// asked for: writing to KSP.log while measuring would cost more than what is being measured.
    /// Modifier (Alt) + F8 dumps what has been recorded so far, Modifier + F7 throws it away.
    /// </summary>
    internal static class Bench
    {
        // One sample per second of game time, so that two runs of the same flight are cut into pieces
        // covering the same stretch of trajectory even if one of them runs slower than the other.
        private const double SampleSeconds = 1.0;
        private const int MaxSamples = 4096;

        // Calibrate: how many times each formula is replayed over a quad, and how often a quad is used
        // for it. One pass over a couple of hundred vertices already lasts far longer than the timer's
        // resolution; the repeats are there to average, not to make the measurement possible. Each of them
        // starts on a quad the formula has not seen, so a formula that keeps something per quad pays for it
        // once per round, the way a real build pays it once per quad.
        private const int CalibrationRounds = 8;
        private const int CalibrateOneQuadIn = 32;

        private static readonly double TicksToNanoseconds = 1e9 / Stopwatch.Frequency;

        private static BenchMode _mode = BenchMode.Off;

        /// <summary>Whether anything is being measured at all.</summary>
        public static bool Recording { get { return _mode != BenchMode.Off; } }

        /// <summary>What was accumulated over one second of game time.</summary>
        private struct Sample
        {
            public double Ut;
            public double UtSpan;
            public double RealSeconds;
            public int Frames;
            public double Altitude;
            public double Speed;
            public int Quads;
            public int TopLevelQuads;
            public long Vertices;
            public long BuildTicks;

            // Of BuildTicks, what went into quads of the highest subdivision level. Comparing two runs on
            // this alone is immune to them not building the same mix of levels.
            public long TopLevelBuildTicks;
            public long UpdateTicks;
            public long SubdivisionSum;
            public int SubdivisionMax;
            public int SpeedLevelCap;
            public int MaxLevel;
        }

        private static readonly Sample[] _samples = new Sample[MaxSamples];
        private static int _sampleCount;
        private static bool _full;

        private static Sample _current;
        private static bool _open;
        private static long _currentStartTicks;
        private static int _warpedSeconds;

        private static int _topLevelQuadsSeen;
        private static int _calibratedQuads;
        private static int _differingQuads;
        private static long _calibratedVertices;

        // Calibrate: the quad as it was found, what stock makes of it, and the inputs every formula is
        // replayed on. Allocated once, on the first calibrated quad.
        private static Vector3[] _savedQuadVerts;
        private static Vector3d[] _savedSphereVerts;
        private static Vector3[] _stockResult;
        private static Vector3d[] _directions;
        private static double[] _heights;

        // Terrain spheres whose subdivision settings have been logged, so that they are logged once.
        private static readonly HashSet<PQS> _describedSpheres = new HashSet<PQS>();

        /// <summary>
        /// Reads benchMode from PluginData/settings.cfg, next to the DLL. A missing file or an unknown
        /// value measures nothing.
        /// </summary>
        public static void LoadSettings()
        {
            string folder = Path.GetDirectoryName(typeof(Bench).Assembly.Location);
            string path = Path.Combine(Path.Combine(folder, "PluginData"), "settings.cfg");
            if (!File.Exists(path))
            {
                return;
            }

            ConfigNode node = ConfigNode.Load(path);
            if (node == null)
            {
                return;
            }

            string mode = node.GetValue("benchMode");
            if (string.IsNullOrEmpty(mode))
            {
                return;
            }

            BenchMode parsed;
            if (Enum.TryParse(mode, true, out parsed) && Enum.IsDefined(typeof(BenchMode), parsed))
            {
                _mode = parsed;
            }
            else
            {
                Log.Warning("Unknown benchMode '" + mode + "' in " + path + ", measuring nothing");
            }
        }

        /// <summary>Announces what is being measured, once the patches are in.</summary>
        public static void Announce()
        {
            // How many vertices a quad holds is not said here: PQS.cacheVertCount is still 0 this early,
            // and only gets its value when the first terrain sphere starts up. The dump reports it.
            Log.Info($"Measuring, benchMode {_mode}."
                + " Alt+F8 dumps what has been recorded, Alt+F7 throws it away.");
            if (Log.IsDebugEnabled)
            {
                Log.Warning("logLevel is above Info, which writes to KSP.log on the very path being"
                    + " measured: the figures would be worthless. Measure at Info.");
            }
        }

        // ==========================================================================
        // What counts as a quad of the highest level
        // ==========================================================================

        /// <summary>
        /// Whether this quad is one of the highest subdivision level: the ones the game detaches into
        /// LocalSpacePQStorage, which carry the collider craft stand on as long as the body's
        /// PQSMod_QuadMeshColliders.maxLevelOffset is 0, and the only ones Terrain Precision Fix corrects.
        /// </summary>
        internal static bool IsTopLevelQuad(PQ quad)
        {
            if (quad == null)
            {
                return false;
            }
            PQS sphere = quad.sphereRoot;

            // Stock has two ways of placing vertices, and only the surface relative one detaches quads
            // this way.
            if (sphere == null || !sphere.surfaceRelativeQuads || sphere.LocalSpacePQStorage == null)
            {
                return false;
            }
            return quad.transform.parent == sphere.LocalSpacePQStorage.transform;
        }

        // ==========================================================================
        // Recording
        // ==========================================================================

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
        private static void RecordQuad(PQS sphere, PQ quad, long ticks)
        {
            if (_describedSpheres.Add(sphere))
            {
                DescribeSphere(sphere);
            }

            bool topLevel = IsTopLevelQuad(quad);
            if (_open)
            {
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
            if (_mode != BenchMode.Calibrate || !topLevel)
            {
                return;
            }
            if (_topLevelQuadsSeen % CalibrateOneQuadIn == 0)
            {
                Calibrate(sphere, quad);
            }
            _topLevelQuadsSeen++;
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

            Log.Info($"BENCH sphere;name={sphere.name};radius={F(sphere.radius, 0)}"
                + $";minLevel={sphere.minLevel};maxLevel={maxLevel}"
                + $";highestLevelUnder={F(highestLevelUnder, 0)}"
                + $";subdivisionOffAbove={F(sphere.maxDetailDistance * sphere.radius, 0)}"
                + $";maxAnglePerSample={angleCap.ToString("E3", CultureInfo.InvariantCulture)}"
                + $";speedCapAt60Fps={F(angleCap * sphere.radius * 60.0, 0)}");

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

        // ==========================================================================
        // Calibrate: what is installed against stock, same data, same frame
        // ==========================================================================

        /// <summary>Places one terrain vertex, the way PQS.BuildVertexSurfaceRelative is asked to.</summary>
        private delegate void VertexPlacer(PQS sphere, PQS.VertexBuildData data);

        // The three things timed, all reached the same way and replayed by the same loop.
        //   installed - the stock method itself, so it runs through whatever Harmony patch is on it today,
        //               or straight to stock when there is none. Nothing here knows which;
        //   stock     - a copy of the stock placement, which stays measurable in a run where the stock
        //               method is patched. It is the yardstick two runs are compared through;
        //   harness   - places nothing. What is left is what the replay itself costs, the fields written
        //               before each call and the indirect call, which the two others also pay.
        private const int FormulaHarness = 0;
        private const int FormulaStock = 1;
        private const int FormulaInstalled = 2;
        private const int FormulaCount = 3;

        private static readonly VertexPlacer[] _place = new VertexPlacer[FormulaCount];
        private static readonly long[] _ticks = new long[FormulaCount];

        private static bool _bindingTried;
        private static bool _calibrationBroken;

        // Whether a calibration is replaying, so that the quad build it re-enters is not taken for a quad
        // the game built.
        private static bool _calibrating;

        // State of PQS the replay has to set for every vertex, because the stock placement reads its
        // inputs from there rather than from the parameter it is handed.
        private static AccessTools.FieldRef<PQS, PQ> _buildQuadField;
        private static AccessTools.FieldRef<PQS, int> _vertexIndexField;
        private static FieldInfo _vbDataField;

        /// <summary>
        /// Binds what the replay needs: the stock vertex placement, and the fields of PQS it reads. A
        /// failure here gives up on calibrating rather than measuring something else.
        /// </summary>
        private static void BindCalibration()
        {
            _bindingTried = true;
            try
            {
                _buildQuadField = AccessTools.FieldRefAccess<PQS, PQ>("buildQuad");
                _vertexIndexField = AccessTools.FieldRefAccess<PQS, int>("vertexIndex");
                _vbDataField = AccessTools.Field(typeof(PQS), "vbData");

                MethodInfo placement = AccessTools.Method(typeof(PQS), "BuildVertexSurfaceRelative",
                    new[] { typeof(PQS.VertexBuildData) });
                if (placement == null)
                {
                    throw new MissingMethodException("PQS", "BuildVertexSurfaceRelative");
                }

                // Bound to the stock method, not to a copy of it. Harmony replaces what that method runs,
                // so this calls whatever is patching it without having to know what that is — and it pays
                // the same wrapper the game pays for it.
                _place[FormulaInstalled] =
                    (VertexPlacer)Delegate.CreateDelegate(typeof(VertexPlacer), placement);
                _place[FormulaStock] = PlaceVertexStock;
                _place[FormulaHarness] = PlaceNothing;
            }
            catch (Exception e)
            {
                _calibrationBroken = true;
                Log.Error($"Could not reach the stock vertex placement, nothing will be calibrated: {e}");
            }
        }

        /// <summary>
        /// Times the vertex placements against each other, replaying each over the vertices of a quad that
        /// has just been built. Leaves the quad, and everything the replay had to set, exactly as found.
        /// </summary>
        private static void Calibrate(PQS sphere, PQ quad)
        {
            if (!_bindingTried)
            {
                BindCalibration();
            }
            if (_calibrationBroken)
            {
                return;
            }

            int count = PQS.cacheVertCount;
            if (quad.verts == null || PQS.verts == null
                || quad.verts.Length < count || PQS.verts.Length < count)
            {
                return;
            }
            // Read once per calibrated quad, well outside the clock: the build fills this same instance
            // for every vertex, and a placement reads its inputs from it.
            PQS.VertexBuildData data = _vbDataField.GetValue(null) as PQS.VertexBuildData;
            if (data == null)
            {
                return;
            }
            EnsureCalibrationBuffers(count);

            // Everything the replay is about to write over. What the terrain ends up looking like, and the
            // state the rest of the build reads next, must not depend on which formula ran last.
            Array.Copy(quad.verts, _savedQuadVerts, count);
            Array.Copy(PQS.verts, _savedSphereVerts, count);
            PQ savedBuildQuad = _buildQuadField(sphere);
            int savedVertexIndex = _vertexIndexField(sphere);
            PQ savedDataQuad = data.buildQuad;
            Vector3d savedDirection = data.directionFromCenter;
            double savedHeight = data.vertHeight;
            int savedVertIndex = data.vertIndex;
            bool savedIsBuilt = quad.isBuilt;

            // What every formula is replayed on, worked out once and outside the clock. PQS.verts holds
            // each vertex as the build left it, which is the direction from the centre of the body times
            // the height along it: the two values a placement is handed.
            for (int index = 0; index < count; index++)
            {
                Vector3d vertex = _savedSphereVerts[index];
                double height = vertex.magnitude;
                _heights[index] = height;
                _directions[index] = height > 0.0 ? vertex / height : Vector3d.zero;
            }

            // The state a real build is in when it hands a vertex over. BuildQuad clears buildQuad on its
            // way out, so it has to be put back for the replay.
            _buildQuadField(sphere) = quad;
            data.buildQuad = quad;

            _calibrating = true;
            try
            {
                if (!InvalidateQuadCaches(sphere, quad))
                {
                    _calibrationBroken = true;
                    Log.Error("Re-entering PQS.BuildQuad rebuilt the quad instead of turning back, so a"
                        + " formula cannot be handed a quad it has not seen: nothing will be calibrated.");
                    return;
                }

                // One untimed round of stock. It warms the two arrays, which the rest of the build has
                // pushed out of cache, so that whichever formula runs first is not charged for it — and it
                // leaves what stock makes of this quad, which the installed placement is compared against
                // below.
                Run(FormulaStock, sphere, data, count);
                Array.Copy(quad.verts, _stockResult, count);

                // The order rotates from one calibrated quad to the next, so that each formula runs as
                // often first as last and whatever is left of that effect does not always land on the same
                // one.
                for (int step = 0; step < FormulaCount; step++)
                {
                    int formula = (_calibratedQuads + step) % FormulaCount;
                    _ticks[formula] += Time(formula, sphere, data, quad, count);
                    if (formula == FormulaInstalled && DiffersFromStock(quad, count))
                    {
                        _differingQuads++;
                    }
                }
                _calibratedQuads++;
                _calibratedVertices += (long)count * CalibrationRounds;
            }
            finally
            {
                _calibrating = false;
                Array.Copy(_savedQuadVerts, quad.verts, count);
                Array.Copy(_savedSphereVerts, PQS.verts, count);
                _buildQuadField(sphere) = savedBuildQuad;
                _vertexIndexField(sphere) = savedVertexIndex;
                data.buildQuad = savedDataQuad;
                data.directionFromCenter = savedDirection;
                data.vertHeight = savedHeight;
                data.vertIndex = savedVertIndex;
                quad.isBuilt = savedIsBuilt;
            }
        }

        private static void EnsureCalibrationBuffers(int count)
        {
            if (_savedQuadVerts != null && _savedQuadVerts.Length >= count)
            {
                return;
            }
            _savedQuadVerts = new Vector3[count];
            _savedSphereVerts = new Vector3d[count];
            _stockResult = new Vector3[count];
            _directions = new Vector3d[count];
            _heights = new double[count];
        }

        /// <summary>
        /// Tells whatever patches the terrain that a build of this quad is starting, so that anything it
        /// keeps per quad is worked out again on the next vertex. Returns whether the call came straight
        /// back, which it must.
        /// </summary>
        private static bool InvalidateQuadCaches(PQS sphere, PQ quad)
        {
            // PQS.BuildQuad turns an already built quad away on its first line, before touching anything —
            // but the Harmony prefixes have run by then, and a quad build starting is the only signal a
            // mod's per-quad state can be expected to listen to. isBuilt is true from the caller's point of
            // view here: BuildQuad has just returned true and PQ.Build is about to set it.
            quad.isBuilt = true;
            return !sphere.BuildQuad(quad);
        }

        private static long Time(int formula, PQS sphere, PQS.VertexBuildData data, PQ quad, int count)
        {
            long total = 0L;
            for (int round = 0; round < CalibrationRounds; round++)
            {
                // Outside the clock, so that a round measures a placement facing a quad it has not seen —
                // paying for it once over a couple of hundred vertices, as a real build does — without the
                // signal that caused it being charged to anyone.
                InvalidateQuadCaches(sphere, quad);
                long start = Stopwatch.GetTimestamp();
                Run(formula, sphere, data, count);
                total += Stopwatch.GetTimestamp() - start;
            }
            return total;
        }

        /// <summary>Replays one placement over every vertex of a quad, once.</summary>
        private static void Run(int formula, PQS sphere, PQS.VertexBuildData data, int count)
        {
            VertexPlacer place = _place[formula];
            for (int index = 0; index < count; index++)
            {
                // The state stock's own loop leaves for each vertex, since a placement reads its inputs
                // from there and not from the parameter it is given. Written the same way for every
                // formula, and what it costs is what the harness formula measures.
                _vertexIndexField(sphere) = index;
                data.vertIndex = index;
                data.directionFromCenter = _directions[index];
                data.vertHeight = _heights[index];
                place(sphere, data);
            }
        }

        /// <summary>The stock placement, line for line as PQS.BuildVertexSurfaceRelative does it.</summary>
        private static void PlaceVertexStock(PQS sphere, PQS.VertexBuildData data)
        {
            // Both Transforms are read per vertex, because stock reads them per vertex: the method this
            // copies is called once for each one, and reads base.transform and buildQuad.transform every
            // time.
            //
            // Stock takes its inputs from private fields of PQS, which a copy cannot reach as cheaply. They
            // are read here off the VertexBuildData the build fills for every vertex anyway: public fields
            // of a class, holding the same values, at the same kind of cost.
            PQ quad = data.buildQuad;
            int index = data.vertIndex;
            Vector3d vertRel = data.directionFromCenter * data.vertHeight;
            Vector3 planetRel = sphere.transform.TransformPoint((Vector3)vertRel);
            PQS.verts[index] = vertRel;
            quad.verts[index] = quad.transform.InverseTransformPoint(planetRel);
        }

        /// <summary>Places nothing: what it measures is what the replay itself costs.</summary>
        private static void PlaceNothing(PQS sphere, PQS.VertexBuildData data)
        {
        }

        /// <summary>
        /// Whether the installed placement put this quad's vertices anywhere other than stock does. Exact,
        /// component by component: what is looked for is any difference at all.
        /// </summary>
        private static bool DiffersFromStock(PQ quad, int count)
        {
            for (int index = 0; index < count; index++)
            {
                Vector3 installed = quad.verts[index];
                Vector3 stock = _stockResult[index];
                if (installed.x != stock.x || installed.y != stock.y || installed.z != stock.z)
                {
                    return true;
                }
            }
            return false;
        }

        // ==========================================================================
        // Reading it back
        // ==========================================================================

        /// <summary>Writes everything recorded so far to KSP.log, as semicolon separated lines.</summary>
        public static void Dump()
        {
            Log.Info($"BENCH begin;mode={_mode};samples={_sampleCount}"
                + $";verticesPerQuad={PQS.cacheVertCount};warpedSeconds={_warpedSeconds}"
                + $";{DescribeInstalled()}");
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
                    I(i),
                    F(s.Ut, 2), F(s.UtSpan, 3), F(s.RealSeconds, 3),
                    I(s.Frames), F(fps, 1),
                    F(s.Altitude, 1), F(s.Speed, 1),
                    I(s.Quads), I(s.TopLevelQuads), L(s.Vertices),
                    F(buildMs, 3), F(topLevelBuildMs, 3), F(updateMs, 3),
                    F(subdivisionAvg, 2), I(s.SubdivisionMax),
                    I(s.SpeedLevelCap == int.MaxValue ? -1 : s.SpeedLevelCap), I(s.MaxLevel)
                }));
            }

            if (_calibratedVertices > 0)
            {
                double harnessNs = _ticks[FormulaHarness] * TicksToNanoseconds / _calibratedVertices;
                double stockNs = _ticks[FormulaStock] * TicksToNanoseconds / _calibratedVertices;
                double installedNs = _ticks[FormulaInstalled] * TicksToNanoseconds / _calibratedVertices;

                // The two figures to read are the net ones: what a placement costs on its own, the replay's
                // own cost taken off both. The raw ones are there so that the subtraction can be checked.
                Log.Info($"BENCH calibration;quads={_calibratedQuads};differingQuads={_differingQuads}"
                    + $";roundsPerQuad={CalibrationRounds};verticesPerFormula={_calibratedVertices}"
                    + $";stockNsPerVertex={F(stockNs - harnessNs, 1)}"
                    + $";installedNsPerVertex={F(installedNs - harnessNs, 1)}"
                    + $";differenceNsPerVertex={F(installedNs - stockNs, 1)}"
                    + $";harnessNsPerVertex={F(harnessNs, 1)}"
                    + $";stockRawNsPerVertex={F(stockNs, 1)}"
                    + $";installedRawNsPerVertex={F(installedNs, 1)}");
                if (_differingQuads == 0)
                {
                    Log.Info("BENCH calibration;the installed placement put every vertex exactly where"
                        + " stock puts it. Either nothing is patching it, or what is changes the cost"
                        + " without changing the terrain.");
                }
            }
            else if (_mode == BenchMode.Calibrate)
            {
                Log.Warning("BENCH calibration;quads=0;nothing was calibrated: "
                    + (_calibrationBroken
                        ? "the replay could not be set up, see the error above"
                        : "no quad of the highest subdivision level was built"));
            }
            Log.Info("BENCH end");
        }

        /// <summary>
        /// Which mods are patching the two stock methods this measurement stands on, so that a log says
        /// for itself which run it is. Read at dump time rather than at startup: nothing says in which
        /// order mods install their patches.
        /// </summary>
        private static string DescribeInstalled()
        {
            return "vertexPlacementPatchedBy="
                + Owners(AccessTools.Method(typeof(PQS), "BuildVertexSurfaceRelative"))
                + ";quadBuildPatchedBy=" + Owners(AccessTools.Method(typeof(PQS), "BuildQuad"));
        }

        /// <summary>The Harmony ids patching a method, in no particular order.</summary>
        private static string Owners(MethodBase method)
        {
            if (method == null)
            {
                return "unknown";
            }
            Patches patches = Harmony.GetPatchInfo(method);
            if (patches == null || patches.Owners == null || patches.Owners.Count == 0)
            {
                return "none";
            }
            return string.Join("+", new List<string>(patches.Owners).ToArray());
        }

        /// <summary>Throws away everything recorded, to start another run without restarting KSP.</summary>
        public static void Reset()
        {
            _sampleCount = 0;
            _full = false;
            _open = false;
            _warpedSeconds = 0;
            Array.Clear(_ticks, 0, _ticks.Length);
            _topLevelQuadsSeen = 0;
            _calibratedQuads = 0;
            _differingQuads = 0;
            _calibratedVertices = 0L;
            _describedSpheres.Clear();
            Log.Info("BENCH reset");
        }

        // The log is read by a spreadsheet or a script, so numbers are written with a dot whatever the
        // machine's locale says.
        private static string F(double value, int decimals)
        {
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        private static string I(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string L(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // ==========================================================================
        // Where the time is taken
        // ==========================================================================

        /// <summary>
        /// Times one terrain quad being built. PQS.BuildQuad is the loop over the vertices of a single
        /// quad, so it runs once per quad actually built, and what it costs includes whatever a mod has
        /// put in the way of the vertex placement.
        /// </summary>
        [HarmonyPatch(typeof(PQS), "BuildQuad")]
        private static class BuildQuadPatch
        {
            private static void Prefix(out long __state)
            {
                __state = Stopwatch.GetTimestamp();
            }

            private static void Postfix(PQS __instance, PQ quad, bool __result, long __state)
            {
                // A false result is a call that returned without building anything, which is what the
                // calibration's own re-entry gets. _calibrating covers the rest.
                if (!__result || quad == null || _calibrating)
                {
                    return;
                }
                RecordQuad(__instance, quad, Stopwatch.GetTimestamp() - __state);
            }
        }

        /// <summary>
        /// Times the whole terrain update of one sphere for one frame, which contains the quad builds
        /// above along with the subdivision decisions and the normals. This is what a frame pays.
        /// </summary>
        [HarmonyPatch(typeof(PQS), "UpdateQuads")]
        private static class UpdateQuadsPatch
        {
            private static void Prefix(out long __state)
            {
                __state = Stopwatch.GetTimestamp();
            }

            private static void Postfix(long __state)
            {
                if (!_open)
                {
                    return;
                }
                _current.UpdateTicks += Stopwatch.GetTimestamp() - __state;
            }
        }
    }

    /// <summary>
    /// Installs the measurement, and only if there is something to measure: with benchMode off, no patch
    /// is applied at all and the terrain runs exactly as it would without this mod.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class PQSBenchMod : MonoBehaviour
    {
        private const string HarmonyId = "com.github.lhervier.ksp.pqsbench";

        private void Start()
        {
            Log.LoadLevel();
            Bench.LoadSettings();
            if (!Bench.Recording)
            {
                Log.Info($"Version {typeof(PQSBenchMod).Assembly.GetName().Version} installed, measuring"
                    + " nothing: set benchMode in PluginData/settings.cfg");
                return;
            }
            try
            {
                new Harmony(HarmonyId).PatchAll(typeof(PQSBenchMod).Assembly);
                Bench.Announce();
            }
            catch (Exception e)
            {
                Log.Error($"Could not install the measurement, nothing will be recorded: {e}");
            }
        }
    }

    /// <summary>Counts frames and listens for the dump and reset keys.</summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class BenchRunner : MonoBehaviour
    {
        // Read through KSP's own key bindings rather than UnityEngine.Input, which lives in a module this
        // mod does not reference.
        private static readonly KeyBinding _dump = new KeyBinding(KeyCode.F8);
        private static readonly KeyBinding _reset = new KeyBinding(KeyCode.F7);

        private void Update()
        {
            if (!Bench.Recording)
            {
                return;
            }
            Bench.OnFrame();
            if (!GameSettings.MODIFIER_KEY.GetKey())
            {
                return;
            }
            if (_dump.GetKeyDown())
            {
                Bench.Dump();
            }
            else if (_reset.GetKeyDown())
            {
                Bench.Reset();
            }
        }
    }
}
