namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>
    /// One way of measuring the terrain, as chosen by benchMode. A single one runs in a game, and it hears
    /// of the terrain only through the events it subscribes to.
    /// </summary>
    internal interface IBench
    {
        /// <summary>
        /// Subscribes to the terrain events this mode measures. Called once, after the patches that raise
        /// them are installed.
        /// </summary>
        void Subscribe();

        /// <summary>Called once per frame, in every scene.</summary>
        void OnFrame();

        /// <summary>Writes what has been recorded so far to KSP.log, as semicolon separated lines.</summary>
        void Dump();

        /// <summary>Throws away everything recorded, to start another run without restarting KSP.</summary>
        void Reset();
    }
}
