namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>What was accumulated over one second of game time.</summary>
    internal struct Sample
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
}
