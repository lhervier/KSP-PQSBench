using UnityEngine;

namespace com.github.lhervier.ksp.pqsbench.bench.calibrate
{
    /// <summary>
    /// Registers the calibrate bench with PQSBenchMod when KSP starts. Does nothing else, and is gone once
    /// it has.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class CalibrateBenchAddon : MonoBehaviour
    {
        private void Awake()
        {
            PQSBenchMod.Register(new CalibrateBench());
            Destroy(gameObject);
        }
    }
}
