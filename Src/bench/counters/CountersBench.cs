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
        // The samples closed over the whole run.
        private readonly SampleSeries _series = new SampleSeries();

        // Whether recording has stopped for the rest of the run, the samples closed before it being kept.
        private bool _stopped;

        private Sample _current;
        private bool _open;
        private long _currentStartTicks;

        /// <summary>
        /// Stops recording for the rest of the run, dropping the sample in progress, and says why. Only the
        /// first call says anything.
        /// </summary>
        private void Stop(string reason)
        {
            if (_stopped)
            {
                return;
            }
            _stopped = true;
            _open = false;
            Log.Warning(reason);
        }

        // =======================================================
        // Subscribe
        // =======================================================

        /// <summary>Listens to every quad built and to every terrain update.</summary>
        public void Subscribe()
        {
            BuildQuadPatch.Built += QuadBuilt;
            UpdateQuadsPatch.Updated += QuadsUpdated;
        }

        // =======================================================
        // On Frame
        // =======================================================

        /// <summary>Counts a frame, and closes the current sample once a second of game time has passed.</summary>
        public void OnFrame()
        {
            if (_stopped)
            {
                return;
            }

            // Leaving flight in the middle of a run is outside the protocol too: the quads other scenes build
            // would pile up in the open sample, which would close on the way back with both scenes mixed.
            if (!HighLogic.LoadedSceneIsFlight)
            {
                if (_open)
                {
                    Stop("Flight scene left, the run no longer follows the protocol: the sample it interrupted is"
                        + " dropped and nothing more is recorded. Dump what was (Alt+F8) and start again (Alt+F7)");
                }
                return;
            }

            // Time warp is outside the protocol: the craft crosses the ground far too fast for a sample to
            // mean anything, and the ground flown over after it is no longer the ground another run flew.
            // The run is given up rather than patched around.
            if (TimeWarp.CurrentRate > Constants.MaxUnwarpedRate)
            {
                Stop("Time warp engaged, the run no longer follows the protocol: the sample it interrupted is"
                    + " dropped and nothing more is recorded. Dump what was (Alt+F8) and start again (Alt+F7)");
                return;
            }

            // Losing the craft in the middle of a run as well: the open sample would go on timing and
            // counting quads without counting frames.
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null)
            {
                if (_open)
                {
                    Stop("Active vessel lost, the run no longer follows the protocol: the sample it interrupted is"
                        + " dropped and nothing more is recorded. Dump what was (Alt+F8) and start again (Alt+F7)");
                }
                return;
            }
            double ut = Planetarium.GetUniversalTime();

            if (_open)
            {
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
                if (_stopped)
                {
                    return;
                }
            }
            Open(ut);
        }

        private void Open(double ut)
        {
            _current = new Sample();
            _current.Ut = ut;
            _current.SpeedLevelCap = int.MaxValue;

            // The clock starts here and stops at the same point of a later frame, so it spans as many frame
            // durations as frames that go by after this one: this frame is counted by the sample it closes,
            // not by the one it opens.
            _current.Frames = 0;
            _currentStartTicks = Stopwatch.GetTimestamp();
            _open = true;
        }

        private void Close()
        {
            if (_series.IsFull)
            {
                Stop(Constants.MaxSamples + " samples recorded, no more room: dump them (Alt+F8) and"
                    + " start again (Alt+F7)");
                return;
            }
            _series.Add(_current);
        }

        // ======================================================
        // Quad built
        // ======================================================

        /// <summary>Records one terrain quad actually built, and the time it took.</summary>
        private void QuadBuilt(PQS sphere, PQ quad, long ticks, bool topLevel)
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

        // ======================================================
        // Quads updated
        // ======================================================

        /// <summary>Records what one terrain update of one sphere cost, for one frame.</summary>
        private void QuadsUpdated(long ticks)
        {
            if (!_open)
            {
                return;
            }
            _current.UpdateTicks += ticks;
        }

        // ======================================================
        // Dump
        // ======================================================

        /// <summary>Writes one line per sample recorded, as semicolon separated values.</summary>
        public void Dump()
        {
            _series.Write();
        }

        // ======================================================
        // Reset
        // ======================================================


        /// <summary>Throws away everything recorded, to start another run without restarting KSP.</summary>
        public void Reset()
        {
            _series.Clear();
            _stopped = false;
            _open = false;
        }
    }
}
