using System;

namespace com.github.lhervier.ksp.pqsbench.bench.calibrate
{
    /// <summary>
    /// Re-entering PQS.BuildQuad on an already built quad rebuilt it instead of turning back, so no formula
    /// can be handed a quad it has not seen.
    /// </summary>
    internal sealed class QuadRebuiltException : Exception
    {
        public QuadRebuiltException()
            : base("Re-entering PQS.BuildQuad rebuilt the quad instead of turning back.")
        {
        }
    }
}
