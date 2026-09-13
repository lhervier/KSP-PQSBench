using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>
    /// Runs the measurement: reads what to measure, installs the patches if there is anything to measure at
    /// all, hands each built quad to whoever is measuring it, and listens for the keys that read the
    /// results back. Modifier (Alt) + F8 dumps what has been recorded so far, Modifier + F7 throws it away.
    ///
    /// What is measured lives in Counters and Calibration; nothing is measured here.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class PQSBenchMod : MonoBehaviour
    {
        private const string HarmonyId = "com.github.lhervier.ksp.pqsbench";

        // Read through KSP's own key bindings rather than UnityEngine.Input, which lives in a module this
        // mod does not reference.
        private static readonly KeyBinding _dump = new KeyBinding(KeyCode.F8);
        private static readonly KeyBinding _reset = new KeyBinding(KeyCode.F7);

        private static BenchMode _mode = BenchMode.Off;

        /// <summary>Whether anything is being measured at all.</summary>
        private static bool Recording { get { return _mode != BenchMode.Off; } }

        private void Start()
        {
            Log.LoadLevel();
            LoadSettings();
            Calibration.Enabled = _mode == BenchMode.Calibrate;
            if (!Recording)
            {
                Log.Info($"Version {typeof(PQSBenchMod).Assembly.GetName().Version} installed, measuring"
                    + " nothing: set benchMode in PluginData/settings.cfg");
                return;
            }
            try
            {
                new Harmony(HarmonyId).PatchAll(typeof(PQSBenchMod).Assembly);
            }
            catch (Exception e)
            {
                Log.Error($"Could not install the measurement, nothing will be recorded: {e}");
                return;
            }

            // KSP instantiates a "once" addon a single time, but does not keep its GameObject across scene
            // loads: without this, the frame count and the keys would stop at the main menu.
            DontDestroyOnLoad(gameObject);
            Announce();
        }

        private void Update()
        {
            if (!Recording)
            {
                return;
            }
            Counters.OnFrame();
            if (!GameSettings.MODIFIER_KEY.GetKey())
            {
                return;
            }
            if (_dump.GetKeyDown())
            {
                Dump();
            }
            else if (_reset.GetKeyDown())
            {
                Reset();
            }
        }

        /// <summary>
        /// Reads benchMode from PluginData/settings.cfg, next to the DLL. A missing file or an unknown
        /// value measures nothing.
        /// </summary>
        private static void LoadSettings()
        {
            string folder = Path.GetDirectoryName(typeof(PQSBenchMod).Assembly.Location);
            string path = Path.Combine(Path.Combine(folder, "PluginData"), "settings.cfg");
            if (!File.Exists(path))
            {
                return;
            }

            ConfigNode node = ConfigNode.Load(path);
            if (node == null)
            {
                return;
            }

            string mode = node.GetValue("benchMode");
            if (string.IsNullOrEmpty(mode))
            {
                return;
            }

            BenchMode parsed;
            if (Enum.TryParse(mode, true, out parsed) && Enum.IsDefined(typeof(BenchMode), parsed))
            {
                _mode = parsed;
            }
            else
            {
                Log.Warning("Unknown benchMode '" + mode + "' in " + path + ", measuring nothing");
            }
        }

        /// <summary>Announces what is being measured, once the patches are in.</summary>
        private static void Announce()
        {
            // How many vertices a quad holds is not said here: PQS.cacheVertCount is still 0 this early,
            // and only gets its value when the first terrain sphere starts up. The dump reports it.
            Log.Info($"Measuring, benchMode {_mode}."
                + " Alt+F8 dumps what has been recorded, Alt+F7 throws it away.");
            if (Log.IsDebugEnabled)
            {
                Log.Warning("logLevel is above Info, which writes to KSP.log on the very path being"
                    + " measured: the figures would be worthless. Measure at Info.");
            }
        }

        /// <summary>Hands one terrain quad the game has just built to whoever is measuring it.</summary>
        internal static void OnQuadBuilt(PQS sphere, PQ quad, long ticks)
        {
            bool topLevel = IsTopLevelQuad(quad);
            Counters.RecordQuad(sphere, quad, ticks, topLevel);
            if (topLevel)
            {
                Calibration.Offer(sphere, quad);
            }
        }

        /// <summary>
        /// Whether this quad is one of the highest subdivision level: the ones the game detaches into
        /// LocalSpacePQStorage, which carry the collider craft stand on as long as the body's
        /// PQSMod_QuadMeshColliders.maxLevelOffset is 0, and the only ones Terrain Precision Fix corrects.
        /// </summary>
        private static bool IsTopLevelQuad(PQ quad)
        {
            if (quad == null)
            {
                return false;
            }
            PQS sphere = quad.sphereRoot;

            // Stock has two ways of placing vertices, and only the surface relative one detaches quads
            // this way.
            if (sphere == null || !sphere.surfaceRelativeQuads || sphere.LocalSpacePQStorage == null)
            {
                return false;
            }
            return quad.transform.parent == sphere.LocalSpacePQStorage.transform;
        }

        /// <summary>Writes everything recorded so far to KSP.log, as semicolon separated lines.</summary>
        private static void Dump()
        {
            Log.Info($"BENCH begin;mode={_mode};samples={Counters.SampleCount}"
                + $";verticesPerQuad={PQS.cacheVertCount};warpedSeconds={Counters.WarpedSeconds}"
                + $";{DescribeInstalled()}");
            Counters.Dump();
            Calibration.Dump();
            Log.Info("BENCH end");
        }

        /// <summary>
        /// Which mods are patching the two stock methods this measurement stands on, so that a log says
        /// for itself which run it is. Read at dump time rather than at startup: nothing says in which
        /// order mods install their patches.
        /// </summary>
        private static string DescribeInstalled()
        {
            return "vertexPlacementPatchedBy="
                + Owners(AccessTools.Method(typeof(PQS), "BuildVertexSurfaceRelative"))
                + ";quadBuildPatchedBy=" + Owners(AccessTools.Method(typeof(PQS), "BuildQuad"));
        }

        /// <summary>The Harmony ids patching a method, in no particular order.</summary>
        private static string Owners(MethodBase method)
        {
            if (method == null)
            {
                return "unknown";
            }
            Patches patches = Harmony.GetPatchInfo(method);
            if (patches == null || patches.Owners == null || patches.Owners.Count == 0)
            {
                return "none";
            }
            return string.Join("+", new List<string>(patches.Owners).ToArray());
        }

        /// <summary>Throws away everything recorded, to start another run without restarting KSP.</summary>
        private static void Reset()
        {
            Counters.Reset();
            Calibration.Reset();
            Log.Info("BENCH reset");
        }
    }
}
