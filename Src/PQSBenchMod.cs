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

        /// <summary>Adds, on a sample of quads, the two vertex placements timed against each other.</summary>
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
        // resolution; the repeats are there to average, not to make the measurement possible.
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

        private static long _stockTicks;
        private static long _fixedTicks;
        private static int _topLevelQuadsSeen;
        private static int _calibratedQuads;
        private static int _refusedQuads;
        private static long _calibratedVertices;
        private static Vector3[] _savedQuadVerts;
        private static Vector3d[] _savedSphereVerts;

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
            Log.Info($"Measuring, benchMode {_mode}, {PQS.cacheVertCount} vertices per quad."
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
        // Calibrate: the two formulas, same data, same frame
        // ==========================================================================

        /// <summary>Places one terrain vertex, and says whether it did.</summary>
        private delegate bool VertexPlacer(PQS sphere, PQ quad, int index, Vector3d vertex);

        /// <summary>Forgets whatever a placement worked out for the quad it was last given.</summary>
        private delegate void QuadReset();

        // Both formulas are called through a delegate of the same type, and both are preceded by a reset
        // of the same type. The fix has to be reached that way, since it lives in another assembly and
        // this mod must run without it; putting stock behind the same indirection means the calibration
        // compares the two formulas rather than the cost of reaching one of them.
        private static VertexPlacer _stockPlace;
        private static QuadReset _stockReset;
        private static VertexPlacer _fixPlace;
        private static QuadReset _fixReset;

        private static bool _bindingTried;

        private static bool CalibrationReady { get { return _fixPlace != null; } }

        // The type calibrate compares stock against, and the Harmony id it patches under.
        private const string FixTypeName = "com.github.lhervier.ksp.terrainprecisionfix.TerrainPrecisionFixMod";
        private const string FixHarmonyId = "com.github.lhervier.ksp.terrainprecisionfix";

        /// <summary>
        /// Looks up the vertex placement of Terrain Precision Fix, if that mod is installed. Both sides are
        /// bound here, so that neither is favoured by how it is reached.
        /// </summary>
        private static void BindCalibration()
        {
            _bindingTried = true;
            _stockPlace = PlaceVertexStock;
            _stockReset = ResetNothing;

            Type fixType = AccessTools.TypeByName(FixTypeName);
            if (fixType == null)
            {
                Log.Warning("Terrain Precision Fix is not installed, so there is no second formula to time"
                    + " against stock: benchMode calibrate records nothing. The counters are unaffected.");
                return;
            }

            // Both are internal to that mod: it exposes nothing for this, on purpose. A delegate bound once
            // costs an indirect call, which is exactly what the stock side pays too.
            MethodInfo place = AccessTools.Method(fixType, "PlaceVertex",
                new[] { typeof(PQS), typeof(PQ), typeof(int), typeof(Vector3d) });
            MethodInfo forget = AccessTools.Method(fixType, "ForgetQuadContext", Type.EmptyTypes);
            if (place == null || forget == null)
            {
                Log.Error("Terrain Precision Fix is installed but does not have the methods this"
                    + " calibration replays. It has probably changed since; benchMode calibrate records"
                    + " nothing.");
                return;
            }

            try
            {
                _fixPlace = (VertexPlacer)Delegate.CreateDelegate(typeof(VertexPlacer), place);
                _fixReset = (QuadReset)Delegate.CreateDelegate(typeof(QuadReset), forget);
            }
            catch (Exception e)
            {
                _fixPlace = null;
                _fixReset = null;
                Log.Error($"Could not reach the vertex placement of Terrain Precision Fix: {e}");
                return;
            }
            Log.Info($"Calibrating against Terrain Precision Fix {fixType.Assembly.GetName().Version}");
        }

        /// <summary>
        /// Times the stock vertex placement against the one Terrain Precision Fix puts in its place,
        /// replaying both over the vertices of a quad that has just been built. Leaves the quad exactly as
        /// it found it.
        /// </summary>
        private static void Calibrate(PQS sphere, PQ quad)
        {
            if (!_bindingTried)
            {
                BindCalibration();
            }
            if (!CalibrationReady)
            {
                return;
            }

            int count = PQS.cacheVertCount;
            if (quad.verts == null || PQS.verts == null
                || quad.verts.Length < count || PQS.verts.Length < count)
            {
                return;
            }

            // Both formulas write where the real build wrote, so what the terrain ends up looking like
            // would otherwise depend on which one happened to run last.
            if (_savedQuadVerts == null || _savedQuadVerts.Length < count)
            {
                _savedQuadVerts = new Vector3[count];
                _savedSphereVerts = new Vector3d[count];
            }
            Array.Copy(quad.verts, _savedQuadVerts, count);
            Array.Copy(PQS.verts, _savedSphereVerts, count);

            // One round of each before the clock starts: the first pass over an array that is not in cache
            // would otherwise be charged to whichever formula runs first. That round also says whether the
            // fix applies to this quad at all — installed but inactive, it would place nothing, and stock
            // would be timed against an empty loop.
            Run(_stockPlace, _stockReset, sphere, quad, count, 1);
            if (Run(_fixPlace, _fixReset, sphere, quad, count, 1) < count)
            {
                Restore(quad, count);
                _refusedQuads++;
                return;
            }

            // The order alternates from one calibrated quad to the next, so that whatever is left of that
            // effect does not always land on the same side.
            if ((_calibratedQuads & 1) == 0)
            {
                _stockTicks += Time(_stockPlace, _stockReset, sphere, quad, count);
                _fixedTicks += Time(_fixPlace, _fixReset, sphere, quad, count);
            }
            else
            {
                _fixedTicks += Time(_fixPlace, _fixReset, sphere, quad, count);
                _stockTicks += Time(_stockPlace, _stockReset, sphere, quad, count);
            }

            Restore(quad, count);
            _calibratedQuads++;
            _calibratedVertices += (long)count * CalibrationRounds;
        }

        private static void Restore(PQ quad, int count)
        {
            Array.Copy(_savedQuadVerts, quad.verts, count);
            Array.Copy(_savedSphereVerts, PQS.verts, count);
        }

        private static long Time(VertexPlacer place, QuadReset reset, PQS sphere, PQ quad, int count)
        {
            long start = Stopwatch.GetTimestamp();
            Run(place, reset, sphere, quad, count, CalibrationRounds);
            return Stopwatch.GetTimestamp() - start;
        }

        /// <summary>
        /// Replays one placement over every vertex of a quad, as many times as asked, and returns how many
        /// vertices its last round placed.
        /// </summary>
        private static int Run(VertexPlacer place, QuadReset reset, PQS sphere, PQ quad, int count, int rounds)
        {
            int placed = 0;
            for (int round = 0; round < rounds; round++)
            {
                // As if the quad had just been handed over: whatever a placement works out once per quad is
                // worked out again here, and charged to the vertices of this round like it is in a real
                // build.
                reset();
                placed = 0;
                for (int index = 0; index < count; index++)
                {
                    if (place(sphere, quad, index, PQS.verts[index]))
                    {
                        placed++;
                    }
                }
            }
            return placed;
        }

        /// <summary>The stock placement, as PQS.BuildVertexSurfaceRelative does it.</summary>
        private static bool PlaceVertexStock(PQS sphere, PQ quad, int index, Vector3d vertex)
        {
            // Stock keeps the vertex relative to the centre of the body alongside the quad-local one: the
            // normals are computed from it. It holds the intermediate in a field of PQS rather than in a
            // local, which is the one liberty taken here, and it is taken on the stock side.
            Vector3 planetRelative = sphere.transform.TransformPoint((Vector3)vertex);
            PQS.verts[index] = vertex;
            quad.verts[index] = quad.transform.InverseTransformPoint(planetRelative);
            return true;
        }

        /// <summary>Stock works nothing out per quad, so its reset has nothing to do.</summary>
        private static void ResetNothing()
        {
        }

        // ==========================================================================
        // Reading it back
        // ==========================================================================

        /// <summary>Writes everything recorded so far to KSP.log, as semicolon separated lines.</summary>
        public static void Dump()
        {
            Log.Info($"BENCH begin;mode={_mode};samples={_sampleCount}"
                + $";verticesPerQuad={PQS.cacheVertCount};warpedSeconds={_warpedSeconds}"
                + $";{DescribeFix()}");
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
                double stock = _stockTicks * TicksToNanoseconds / _calibratedVertices;
                double patched = _fixedTicks * TicksToNanoseconds / _calibratedVertices;
                Log.Info($"BENCH calibration;quads={_calibratedQuads};refusedQuads={_refusedQuads}"
                    + $";verticesPerFormula={_calibratedVertices}"
                    + $";stockNsPerVertex={F(stock, 1)};fixedNsPerVertex={F(patched, 1)}"
                    + $";differenceNsPerVertex={F(patched - stock, 1)}");
            }
            else if (_mode == BenchMode.Calibrate)
            {
                Log.Warning($"BENCH calibration;quads=0;refusedQuads={_refusedQuads};nothing was"
                    + " calibrated: either no quad of the highest level was built, or the fix declined"
                    + " every one of them");
            }
            Log.Info("BENCH end");
        }

        /// <summary>
        /// Whether Terrain Precision Fix is installed, and whether it is actually patching the vertex
        /// placement. Read here rather than at startup: nothing says which of the two mods loads first.
        /// </summary>
        private static string DescribeFix()
        {
            Type fixType = AccessTools.TypeByName(FixTypeName);
            if (fixType == null)
            {
                return "fix=absent";
            }

            // Installed is not the same as working: the fix gives up on its own if it cannot bind what it
            // patches, and then leaves the terrain exactly as stock builds it.
            bool patching = false;
            MethodBase target = AccessTools.Method(typeof(PQS), "BuildVertexSurfaceRelative");
            if (target != null)
            {
                Patches patches = Harmony.GetPatchInfo(target);
                patching = patches != null && patches.Owners != null && patches.Owners.Contains(FixHarmonyId);
            }
            return $"fix={fixType.Assembly.GetName().Version};fixPatching={patching}";
        }

        /// <summary>Throws away everything recorded, to start another run without restarting KSP.</summary>
        public static void Reset()
        {
            _sampleCount = 0;
            _full = false;
            _open = false;
            _warpedSeconds = 0;
            _stockTicks = 0L;
            _fixedTicks = 0L;
            _topLevelQuadsSeen = 0;
            _calibratedQuads = 0;
            _refusedQuads = 0;
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
                // A false result is a call that returned without building anything.
                if (!__result || quad == null)
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
