using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using com.github.lhervier.ksp.pqsbench.bench.calibrate;
using com.github.lhervier.ksp.pqsbench.bench.counters;
using HarmonyLib;
using UnityEngine;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>
    /// Runs the measurement: reads what to measure, installs the terrain patches, lets the chosen mode
    /// subscribe to what they raise, and listens for the keys that read the results back. Modifier (Alt)
    /// + F8 dumps what has been recorded so far, Modifier + F7 throws it away.
    ///
    /// The two modes never run together: a mode measures its own thing and nothing else, so that its log
    /// holds no figure the mode itself has disturbed. What each of them measures lives in CountersBench and
    /// CalibrateBench; nothing is measured here.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class PQSBenchMod : MonoBehaviour
    {
        // Read through KSP's own key bindings rather than UnityEngine.Input, which lives in a module this
        // mod does not reference.
        private static readonly KeyBinding _dump = new KeyBinding(Constants.DumpKey);
        private static readonly KeyBinding _reset = new KeyBinding(Constants.ResetKey);

        private static EBenchMode _mode = EBenchMode.Off;

        // The mode measuring, or null when nothing is.
        private static IBench _bench;

        /// <summary>Whether anything is being measured at all.</summary>
        private static bool Recording { get { return _bench != null; } }

        private void Start()
        {
            Log.LoadLevel();
            LoadSettings();
            _bench = CreateBench(_mode);
            if (!Recording)
            {
                Log.Info($"Version {typeof(PQSBenchMod).Assembly.GetName().Version} installed, measuring"
                    + " nothing: set benchMode in PluginData/settings.cfg");
                return;
            }
            try
            {
                // Every patch in both modes, whether the mode listens to it or not. One nobody listens to
                // reads the clock twice per call and records nothing, outside anything calibrate times.
                new Harmony(Constants.HarmonyId).PatchAll(typeof(PQSBenchMod).Assembly);
                _bench.Subscribe();
            }
            catch (Exception e)
            {
                Log.Error($"Could not install the measurement, nothing will be recorded: {e}");
                _bench = null;
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
            RunInfo.NoteFrame();
            _bench.OnFrame();
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
            string path = Path.Combine(Path.Combine(folder, Constants.SettingsFolder), Constants.SettingsFile);
            if (!File.Exists(path))
            {
                return;
            }

            ConfigNode node = ConfigNode.Load(path);
            if (node == null)
            {
                return;
            }

            string mode = node.GetValue(Constants.BenchModeSetting);
            if (string.IsNullOrEmpty(mode))
            {
                return;
            }

            EBenchMode parsed;
            if (Enum.TryParse(mode, true, out parsed) && Enum.IsDefined(typeof(EBenchMode), parsed))
            {
                _mode = parsed;
            }
            else
            {
                Log.Warning("Unknown benchMode '" + mode + "' in " + path + ", measuring nothing");
            }
        }

        /// <summary>The mode a benchMode stands for, or null for one that measures nothing.</summary>
        private static IBench CreateBench(EBenchMode mode)
        {
            switch (mode)
            {
                case EBenchMode.Counters:
                    return new CountersBench();
                case EBenchMode.Calibrate:
                    return new CalibrateBench();
                default:
                    return null;
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

        /// <summary>Writes everything recorded so far to KSP.log, as semicolon separated lines.</summary>
        private static void Dump()
        {
            Log.Info($"BENCH begin;mode={_mode};{DescribeInstalled()}");
            RunInfo.Dump();
            _bench.Dump();
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
            RunInfo.Reset();
            _bench.Reset();
            Log.Info("BENCH reset");
        }
    }
}
