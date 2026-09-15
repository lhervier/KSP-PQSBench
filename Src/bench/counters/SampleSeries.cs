using System.Diagnostics;

namespace com.github.lhervier.ksp.pqsbench.bench.counters
{
    /// <summary>
    /// The samples closed since the last Clear, in the order they were added, up to Constants.MaxSamples.
    /// Meant to be allocated once, and cleared to start another run.
    /// </summary>
    internal sealed class SampleSeries
    {
        private readonly Sample[] _samples = new Sample[Constants.MaxSamples];
        private int _count;

        /// <summary>Whether no more sample can be added.</summary>
        public bool IsFull => _count >= Constants.MaxSamples;

        /// <summary>Adds one closed sample. Must not be called when IsFull.</summary>
        public void Add(Sample sample)
        {
            _samples[_count++] = sample;
        }

        /// <summary>
        /// Writes the samples as semicolon separated values: a line giving their number, a header, then one
        /// line per sample.
        /// </summary>
        public void Write()
        {
            Log.Info($"BENCH counters;samples={FormatUtils.I(_count)}");
            Log.Info("BENCH;sample;ut;utSpan;realSeconds;frames;fps;altitude;speed;quads;topLevelQuads"
                + ";vertices;buildMs;topLevelBuildMs;updateMs;subdivisionAvg;subdivisionMax;speedLevelCap;maxLevel");
            for (int i = 0; i < _count; i++)
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
                    FormatUtils.I(i),
                    FormatUtils.F(s.Ut, 2),
                    FormatUtils.F(s.UtSpan, 3),
                    FormatUtils.F(s.RealSeconds, 3),
                    FormatUtils.I(s.Frames),
                    FormatUtils.F(fps, 1),
                    FormatUtils.F(s.Altitude, 1),
                    FormatUtils.F(s.Speed, 1),
                    FormatUtils.I(s.Quads),
                    FormatUtils.I(s.TopLevelQuads),
                    FormatUtils.L(s.Vertices),
                    FormatUtils.F(buildMs, 3),
                    FormatUtils.F(topLevelBuildMs, 3),
                    FormatUtils.F(updateMs, 3),
                    FormatUtils.F(subdivisionAvg, 2),
                    FormatUtils.I(s.SubdivisionMax),
                    FormatUtils.I(s.SpeedLevelCap == int.MaxValue ? -1 : s.SpeedLevelCap),
                    FormatUtils.I(s.MaxLevel)
                }));
            }
        }

        /// <summary>Throws away every sample added.</summary>
        public void Clear()
        {
            _count = 0;
        }
    }
}
