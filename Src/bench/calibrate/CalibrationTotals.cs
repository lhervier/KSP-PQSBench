using System;
using System.Diagnostics;

namespace com.github.lhervier.ksp.pqsbench.bench.calibrate
{
    /// <summary>
    /// What the calibration has timed since the last Clear, over whole quads only: every formula timed on
    /// each of them, over the same vertices. Meant to be allocated once, and cleared to start another run.
    /// </summary>
    internal sealed class CalibrationTotals
    {
        private static readonly double TicksToNanoseconds = 1e9 / Stopwatch.Frequency;

        // One per formula, indexed by Constants.FormulaHarness, FormulaStock and FormulaInstalled.
        private readonly long[] _ticks = new long[Constants.FormulaCount];
        private int _quads;
        private long _vertices;

        /// <summary>How many quads were added since the last Clear.</summary>
        public int Quads => _quads;

        /// <summary>Whether nothing was added since the last Clear.</summary>
        public bool IsEmpty => _vertices == 0L;

        /// <summary>
        /// Adds one quad on which every formula was timed, Constants.Rounds times over its vertexCount
        /// vertices. quadTicks gives the stopwatch ticks of each formula, indexed like the formulas.
        /// </summary>
        public void AddQuad(long[] quadTicks, int vertexCount)
        {
            for (int formula = 0; formula < Constants.FormulaCount; formula++)
            {
                _ticks[formula] += quadTicks[formula];
            }
            _quads++;
            _vertices += (long)vertexCount * Constants.Rounds;
        }

        /// <summary>
        /// Writes the totals as one line of semicolon separated values. Only meaningful when something was
        /// added.
        /// </summary>
        public void Write()
        {
            // Each variable is named after the column it is written to.
            double harnessNsPerVertex = _ticks[Constants.FormulaHarness] * TicksToNanoseconds / _vertices;
            double stockRawNsPerVertex = _ticks[Constants.FormulaStock] * TicksToNanoseconds / _vertices;
            double installedRawNsPerVertex = _ticks[Constants.FormulaInstalled] * TicksToNanoseconds / _vertices;

            // The two figures to read are the net ones: what a placement costs on its own, the replay's own
            // cost taken off both. The raw ones are there so that the subtraction can be checked.
            double stockNsPerVertex = stockRawNsPerVertex - harnessNsPerVertex;
            double installedNsPerVertex = installedRawNsPerVertex - harnessNsPerVertex;
            double differenceNsPerVertex = installedRawNsPerVertex - stockRawNsPerVertex;

            Log.Info($"BENCH calibration;quads={_quads}"
                + $";roundsPerQuad={Constants.Rounds};verticesPerFormula={_vertices}"
                + $";stockNsPerVertex={FormatUtils.F(stockNsPerVertex, 1)}"
                + $";installedNsPerVertex={FormatUtils.F(installedNsPerVertex, 1)}"
                + $";differenceNsPerVertex={FormatUtils.F(differenceNsPerVertex, 1)}"
                + $";harnessNsPerVertex={FormatUtils.F(harnessNsPerVertex, 1)}"
                + $";stockRawNsPerVertex={FormatUtils.F(stockRawNsPerVertex, 1)}"
                + $";installedRawNsPerVertex={FormatUtils.F(installedRawNsPerVertex, 1)}");
        }

        /// <summary>Throws away everything added.</summary>
        public void Clear()
        {
            Array.Clear(_ticks, 0, _ticks.Length);
            _quads = 0;
            _vertices = 0L;
        }
    }
}
