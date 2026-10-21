using UnityEngine;

namespace com.github.lhervier.ksp.util.pqsbench.bench.calibrate
{
    /// <summary>
    /// Registers the calibrate bench with KSPUtilPQSBench when KSP starts. Does nothing else, and is gone once
    /// it has.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    internal class CalibrateBenchAddon : MonoBehaviour
    {
        private void Awake()
        {
            KSPUtilPQSBench.Register(new CalibrateBench());
            Destroy(gameObject);
        }
    }
}
