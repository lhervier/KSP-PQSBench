using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.pqsbench.bench.calibrate
{
    /// <summary>
    /// What one terrain vertex costs: on a sample of quads, and in the frame that built them, whatever is
    /// patching the vertex placement is timed against the stock one, on the same data. This is
    /// the whole of what the calibrate mode measures, and nothing else runs alongside it.
    ///
    /// Nothing here knows which mod is installed, or whether one is at all. The quad is left exactly as it
    /// was found, so the terrain does not depend on anything measured here.
    /// </summary>
    internal sealed class CalibrateBench : IBench
    {
        private static readonly double TicksToNanoseconds = 1e9 / Stopwatch.Frequency;

        /// <summary>Places one terrain vertex, the way PQS.BuildVertexSurfaceRelative is asked to.</summary>
        private delegate void VertexPlacer(PQS sphere, PQS.VertexBuildData data);

        // One per formula, indexed by Constants.FormulaHarness, FormulaStock and FormulaInstalled.
        private readonly VertexPlacer[] _place = new VertexPlacer[Constants.FormulaCount];
        private readonly long[] _ticks = new long[Constants.FormulaCount];

        // What each formula took on the quad being calibrated, only added to _ticks once every formula has
        // been timed on it.
        private readonly long[] _quadTicks = new long[Constants.FormulaCount];

        // Whether re-entering PQS.BuildQuad was found to rebuild the quad instead of turning back, which can
        // only be seen on a real quad and gives up on calibrating for the rest of the game.
        private bool _broken;

        // Whether a calibration is replaying, so that the quad build it re-enters is not taken for a quad
        // the game built.
        private bool _replaying;

        private int _topLevelQuadsSeen;
        private int _calibratedQuads;
        private long _calibratedVertices;

        // The quad as it was found, and the inputs every formula is replayed on. Allocated once, on the first
        // calibrated quad.
        private Vector3[] _savedQuadVerts;
        private Vector3d[] _savedSphereVerts;
        private Vector3d[] _directions;
        private double[] _heights;

        // State of PQS the replay has to set for every vertex, because the stock placement reads its
        // inputs from there rather than from the parameter it is handed.
        private AccessTools.FieldRef<PQS, PQ> _buildQuadField;
        private AccessTools.FieldRef<PQS, int> _vertexIndexField;
        private FieldInfo _vbDataField;

        // ============================================================
        // Subscribe
        // ============================================================

        /// <summary>
        /// Binds what the replay needs, then listens to every quad built. The terrain update of a frame is
        /// not listened to. Throws, listening to nothing, if the stock vertex placement or the fields of PQS
        /// it reads cannot be reached.
        /// </summary>
        public void Subscribe()
        {
            Bind();
            BuildQuadPatch.Built += QuadBuilt;
        }

        /// <summary>
        /// Binds what the replay needs: the stock vertex placement, and the fields of PQS it reads. Throws
        /// if any of them cannot be reached, rather than measuring something else.
        /// </summary>
        private void Bind()
        {
            _buildQuadField = AccessTools.FieldRefAccess<PQS, PQ>("buildQuad");
            _vertexIndexField = AccessTools.FieldRefAccess<PQS, int>("vertexIndex");

            // AccessTools.Field returns null rather than throwing, which would only surface inside a quad
            // build.
            _vbDataField = AccessTools.Field(typeof(PQS), "vbData");
            if (_vbDataField == null)
            {
                throw new MissingFieldException("PQS", "vbData");
            }

            MethodInfo placement = FindPQSBuildVertexSurfaceRelativeMethod();
            _place[Constants.FormulaHarness] = HarnessPlacer;
            _place[Constants.FormulaStock] = BindStockPlacer(placement);
            _place[Constants.FormulaInstalled] = BindInstalledPlacer(placement);
        }

        /// <summary>The game's vertex placement, PQS.BuildVertexSurfaceRelative. Throws if it is not there.</summary>
        private static MethodInfo FindPQSBuildVertexSurfaceRelativeMethod()
        {
            MethodInfo placement = AccessTools.Method(
                typeof(PQS),
                "BuildVertexSurfaceRelative",
                new[] { typeof(PQS.VertexBuildData) }
            );
            if (placement == null)
            {
                throw new MissingMethodException("PQS", "BuildVertexSurfaceRelative");
            }
            return placement;
        }

        /// <summary>Places nothing: what it measures is what the replay itself costs.</summary>
        private static void HarnessPlacer(PQS sphere, PQS.VertexBuildData data)
        {
        }

        /// <summary>
        /// The stock vertex placement, still reachable in a run where the real method is patched. Only
        /// valid once BindStockPlacer has run.
        /// </summary>
        private static void StockPlacer(PQS sphere, PQS.VertexBuildData data)
        {
            // BindStockPlacer replaces this body with the original IL of PQS.BuildVertexSurfaceRelative, so
            // what runs here is the stock placement itself and cannot drift from it: the same private fields
            // of PQS read in the same order, none of which a hand-written copy can reach without paying for
            // it.
            throw new NotImplementedException("Harmony fills this in from the stock method");
        }

        /// <summary>
        /// Makes StockPlacer run the unpatched body of the given placement, whatever is patching it, and
        /// returns it.
        /// </summary>
        private static VertexPlacer BindStockPlacer(MethodInfo placement)
        {
            // The yardstick is not a transcription of the stock placement either: Harmony copies the
            // original method's IL into StockPlacer, so it reads the same fields in the same order,
            // whatever is patching the real method today. A hand-written copy cannot reach those fields as
            // cheaply and measured 8 % low.
            new Harmony(Constants.HarmonyId)
                .CreateReversePatcher(
                    placement,
                    new HarmonyMethod(
                        AccessTools.Method(
                            typeof(CalibrateBench),
                            nameof(StockPlacer)
                        )
                    )
                )
                .Patch();
            return StockPlacer;
        }

        /// <summary>Returns a placer that calls the given placement as the game does, patches included.</summary>
        private static VertexPlacer BindInstalledPlacer(MethodInfo placement)
        {
            // Bound to the stock method, not to a copy of it. Harmony replaces what that method runs, so
            // this calls whatever is patching it without having to know what that is — and it pays the
            // same wrapper the game pays for it.
            return (VertexPlacer) Delegate.CreateDelegate(
                typeof(VertexPlacer),
                placement
            );
        }

        // ============================================================
        // OnFrame
        // ============================================================
        
        /// <summary>Does nothing: the calibration only ever runs inside a quad build.</summary>
        public void OnFrame()
        {
        }

        // ============================================================
        // Quad built event
        // ============================================================

        /// <summary>
        /// Offers one freshly built quad, which is calibrated or not depending on how many of the highest
        /// subdivision level have gone by. A quad of any other level is ignored, and so is one the
        /// calibration itself made the game build. Its build time is not used.
        /// </summary>
        private void QuadBuilt(PQS sphere, PQ quad, long ticks, bool topLevel)
        {
            if (!topLevel || _replaying)
            {
                return;
            }
            if (_topLevelQuadsSeen % Constants.OneQuadIn == 0)
            {
                Calibrate(sphere, quad);
            }
            _topLevelQuadsSeen++;
        }

        /// <summary>
        /// Times the vertex placements against each other, replaying each over the vertices of a quad that
        /// has just been built. Leaves the quad, and everything the replay had to set, exactly as found.
        /// </summary>
        private void Calibrate(PQS sphere, PQ quad)
        {
            if (_broken)
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

            int count = PQS.cacheVertCount;
            if (quad.verts == null || PQS.verts == null || quad.verts.Length < count || PQS.verts.Length < count)
            {
                return;
            }
            EnsureBuffers(count);

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

            _replaying = true;
            try
            {
                // One untimed round of stock. It warms the two arrays, which the rest of the build has
                // pushed out of cache, so that whichever formula runs first is not charged for it.
                Run(Constants.FormulaStock, sphere, data, count);

                // The order rotates from one calibrated quad to the next, so that each formula runs as
                // often first as last and whatever is left of that effect does not always land on the same
                // one.
                for (int step = 0; step < Constants.FormulaCount; step++)
                {
                    int formula = (_calibratedQuads + step) % Constants.FormulaCount;
                    if (!MeasureTicks(formula, sphere, data, quad, count, out _quadTicks[formula]))
                    {
                        // Nothing of this quad is kept: the formulas already timed on it would otherwise
                        // be counted without its vertices.
                        _broken = true;
                        Log.Error("Re-entering PQS.BuildQuad rebuilt the quad instead of turning back, so a"
                            + " formula cannot be handed a quad it has not seen: nothing more will be"
                            + " calibrated.");
                        return;
                    }
                }
                for (int formula = 0; formula < Constants.FormulaCount; formula++)
                {
                    _ticks[formula] += _quadTicks[formula];
                }
                _calibratedQuads++;
                _calibratedVertices += (long)count * Constants.Rounds;
            }
            finally
            {
                _replaying = false;
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

        private void EnsureBuffers(int count)
        {
            if (_savedQuadVerts != null && _savedQuadVerts.Length >= count)
            {
                return;
            }
            _savedQuadVerts = new Vector3[count];
            _savedSphereVerts = new Vector3d[count];
            _directions = new Vector3d[count];
            _heights = new double[count];
        }

        /// <summary>
        /// Replays one placement over every vertex of a quad, once per round, and gives the stopwatch ticks
        /// spent in the replays alone. Returns false, stopping at that round, if re-entering PQS.BuildQuad
        /// rebuilt the quad instead of turning back: the ticks given are then meaningless.
        /// </summary>
        private bool MeasureTicks(
            int formula, 
            PQS sphere, 
            PQS.VertexBuildData data, 
            PQ quad, 
            int count,
            out long ticks
        ) {
            ticks = 0L;
            for (int round = 0; round < Constants.Rounds; round++)
            {
                // Outside the clock, so that a round measures a placement facing a quad it has not seen —
                // paying for it once over a couple of hundred vertices, as a real build does — without the
                // signal that caused it being charged to anyone. Checked on every round rather than once
                // per quad: whatever patches BuildQuad is not bound to answer the same way twice.
                if (!InvalidateQuadCaches(sphere, quad))
                {
                    return false;
                }
                long start = Stopwatch.GetTimestamp();
                Run(formula, sphere, data, count);
                ticks += Stopwatch.GetTimestamp() - start;
            }
            return true;
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

        /// <summary>Replays one placement over every vertex of a quad, once.</summary>
        private void Run(int formula, PQS sphere, PQS.VertexBuildData data, int count)
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

        // ===========================================================
        // Dump
        // ===========================================================

        /// <summary>Writes what was timed, or why nothing was, as semicolon separated values.</summary>
        public void Dump()
        {
            if (_calibratedVertices > 0)
            {
                // Each variable is named after the column it is written to.
                double harnessNsPerVertex = _ticks[Constants.FormulaHarness] * TicksToNanoseconds / _calibratedVertices;
                double stockRawNsPerVertex = _ticks[Constants.FormulaStock] * TicksToNanoseconds / _calibratedVertices;
                double installedRawNsPerVertex = _ticks[Constants.FormulaInstalled] * TicksToNanoseconds / _calibratedVertices;

                // The two figures to read are the net ones: what a placement costs on its own, the replay's
                // own cost taken off both. The raw ones are there so that the subtraction can be checked.
                double stockNsPerVertex = stockRawNsPerVertex - harnessNsPerVertex;
                double installedNsPerVertex = installedRawNsPerVertex - harnessNsPerVertex;
                double differenceNsPerVertex = installedRawNsPerVertex - stockRawNsPerVertex;

                Log.Info($"BENCH calibration;quads={_calibratedQuads}"
                    + $";roundsPerQuad={Constants.Rounds};verticesPerFormula={_calibratedVertices}"
                    + $";stockNsPerVertex={FormatUtils.F(stockNsPerVertex, 1)}"
                    + $";installedNsPerVertex={FormatUtils.F(installedNsPerVertex, 1)}"
                    + $";differenceNsPerVertex={FormatUtils.F(differenceNsPerVertex, 1)}"
                    + $";harnessNsPerVertex={FormatUtils.F(harnessNsPerVertex, 1)}"
                    + $";stockRawNsPerVertex={FormatUtils.F(stockRawNsPerVertex, 1)}"
                    + $";installedRawNsPerVertex={FormatUtils.F(installedRawNsPerVertex, 1)}");
            }
            else
            {
                Log.Warning(
                    "BENCH calibration;quads=0;nothing was calibrated: " + 
                    (
                        _broken
                            ? "re-entering PQS.BuildQuad rebuilt the quad, see the error above"
                            : "no quad of the highest subdivision level was built"
                    )
                );
            }
        }

        // ==========================================================
        // Reset
        // ==========================================================

        /// <summary>Throws away everything timed, to start another run without restarting KSP.</summary>
        public void Reset()
        {
            Array.Clear(_ticks, 0, _ticks.Length);
            _topLevelQuadsSeen = 0;
            _calibratedQuads = 0;
            _calibratedVertices = 0L;
        }
    }
}
