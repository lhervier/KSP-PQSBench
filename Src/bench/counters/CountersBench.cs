using System.Diagnostics;

namespace com.github.lhervier.ksp.pqsbench.bench.counters
{
    /// <summary>
    /// What the terrain costs in flight: how many quads the game builds, how long each one takes, and how
    /// much of a frame that is, cut into samples of one second of game time. This is the whole of what the
    /// counters mode measures, and nothing else runs alongside it.
    ///
    /// Everything is accumulated in memory, in arrays allocated once, and nothing is written until it is
    /// asked for: writing to KSP.log while measuring would cost more than what is being measured.
    /// </summary>
    internal sealed class CountersBench : IBench
    {
        private readonly Sample[] _samples = new Sample[Constants.MaxSamples];
        private int _sampleCount;
        private bool _full;

        private Sample _current;
        private bool _open;
        private long _currentStartTicks;
        private int _warpedSeconds;

        /// <summary>Listens to every quad built and to every terrain update.</summary>
        public void Subscribe()
        {
            BuildQuadPatch.Built += RecordQuad;
            UpdateQuadsPatch.Updated += RecordUpdate;
        }

        /// <summary>Counts a frame, and closes the current sample once a second of game time has passed.</summary>
        public void OnFrame()
        {
            if (_full || !HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            // Time warp is not measured at all: the craft crosses the ground far too fast for a sample to
            // mean anything, and a sample per second of game time would be hundreds of samples per second.
            if (TimeWarp.CurrentRate > Constants.MaxUnwarpedRate)
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
            if (ut - _current.Ut < Constants.SampleSeconds)
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

        private void Open(double ut)
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

        private void Close()
        {
            if (_sampleCount >= Constants.MaxSamples)
            {
                if (!_full)
                {
                    _full = true;
                    Log.Warning(Constants.MaxSamples +" samples recorded, no more room: dump them (Alt+F8) and"
                        + " start again (Alt+F7)");
                }
                _open = false;
                return;
            }
            _samples[_sampleCount++] = _current;
        }

        /// <summary>Records one terrain quad actually built, and the time it took.</summary>
        private void RecordQuad(PQS sphere, PQ quad, long ticks, bool topLevel)
        {
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
        private void RecordUpdate(long ticks)
        {
            if (!_open)
            {
                return;
            }
            _current.UpdateTicks += ticks;
        }

        /// <summary>Writes one line per sample recorded, as semicolon separated values.</summary>
        public void Dump()
        {
            Log.Info($"BENCH counters;samples={FormatUtils.I(_sampleCount)};warpedSeconds={FormatUtils.I(_warpedSeconds)}");
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
                    FormatUtils.I(i),
                    FormatUtils.F(s.Ut, 2), FormatUtils.F(s.UtSpan, 3), FormatUtils.F(s.RealSeconds, 3),
                    FormatUtils.I(s.Frames), FormatUtils.F(fps, 1),
                    FormatUtils.F(s.Altitude, 1), FormatUtils.F(s.Speed, 1),
                    FormatUtils.I(s.Quads), FormatUtils.I(s.TopLevelQuads), FormatUtils.L(s.Vertices),
                    FormatUtils.F(buildMs, 3), FormatUtils.F(topLevelBuildMs, 3), FormatUtils.F(updateMs, 3),
                    FormatUtils.F(subdivisionAvg, 2), FormatUtils.I(s.SubdivisionMax),
                    FormatUtils.I(s.SpeedLevelCap == int.MaxValue ? -1 : s.SpeedLevelCap), FormatUtils.I(s.MaxLevel)
                }));
            }
        }

        /// <summary>Throws away everything recorded, to start another run without restarting KSP.</summary>
        public void Reset()
        {
            _sampleCount = 0;
            _full = false;
            _open = false;
            _warpedSeconds = 0;
        }
    }
}
