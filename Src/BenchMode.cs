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
}
