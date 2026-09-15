namespace com.github.lhervier.ksp.pqsbench.bench.calibrate
{
    /// <summary>
    /// What every formula is replayed on: each vertex of a quad, as the direction from the centre of the
    /// body and the height along it, both at the same index.
    /// </summary>
    internal struct ReplayInputs
    {
        public Vector3d[] Directions;
        public double[] Heights;
    }
}
