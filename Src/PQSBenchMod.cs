using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using com.github.lhervier.ksp.pqsbench.events;
using com.github.lhervier.ksp.pqsbench.utils;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>
    /// Runs the measurement as soon as the mod is installed: installs the terrain patch, subscribes every
    /// bench registered with it to what that patch raises, and listens for the keys that read the results
    /// back. Modifier (Alt) + F8 dumps what has been recorded so far, Modifier + F7 throws it away.
    ///
    /// What is measured lives in the benches; nothing is measured here.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class PQSBenchMod : MonoBehaviour
    {
        // Read through KSP's own key bindings rather than UnityEngine.Input, which lives in a module this
        // mod does not reference.
        private static readonly KeyBinding _dump = new KeyBinding(Constants.DumpKey);
        private static readonly KeyBinding _reset = new KeyBinding(Constants.ResetKey);

        // The benches registered before this addon started, waiting to be subscribed by Start.
        private static readonly List<IBench> _registered = new List<IBench>();

        // The benches subscribed, the only ones dumped and reset.
        private static readonly List<IBench> _benches = new List<IBench>();

        // Whether Start has installed the terrain patch: from then on, a bench is subscribed as soon as it
        // registers.
        private static bool _started;

        /// <summary>Whether anything is being measured at all.</summary>
        private static bool Recording { get { return _benches.Count > 0; } }

        /// <summary>
        /// Registers a bench and subscribes it to the terrain events: at once if this addon has started,
        /// otherwise when it does. A bench that cannot subscribe records nothing, and says so in KSP.log.
        /// </summary>
        internal static void Register(IBench bench)
        {
            if (!_started)
            {
                _registered.Add(bench);
                return;
            }
            Subscribe(bench);
        }

        /// <summary>Subscribes one bench, or says in KSP.log why it could not be.</summary>
        private static void Subscribe(IBench bench)
        {
            try
            {
                bench.Subscribe();
            }
            catch (Exception e)
            {
                // A bench listens to nothing when its Subscribe throws, and the others go on without it.
                Log.Error($"Could not install {bench.GetType().Name}, it will record nothing: {e}");
                return;
            }
            _benches.Add(bench);
        }

        private void Start()
        {
            Log.LoadLevel();
            try
            {
                new Harmony(Constants.HarmonyId).PatchAll(typeof(PQSBenchMod).Assembly);
                BuildQuadPatch.Built += RunInfo.QuadBuilt;
            }
            catch (Exception e)
            {
                Log.Error($"Could not install the measurement, nothing will be recorded: {e}");
                return;
            }
            _started = true;

            // The benches that registered before this addon started.
            foreach (IBench bench in _registered)
            {
                Subscribe(bench);
            }
            _registered.Clear();

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

        /// <summary>Announces what is being measured, once the patches are in.</summary>
        private static void Announce()
        {
            // How many vertices a quad holds is not said here: PQS.cacheVertCount is still 0 this early,
            // and only gets its value when the first terrain sphere starts up. The dump reports it.
            Log.Info($"Version {typeof(PQSBenchMod).Assembly.GetName().Version} installed, measuring."
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
            Log.Info($"BENCH begin;{DescribeInstalled()}");
            RunInfo.Dump();
            foreach (IBench bench in _benches)
            {
                bench.Dump();
            }
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
            foreach (IBench bench in _benches)
            {
                bench.Reset();
            }
            Log.Info("BENCH reset");
        }
    }
}
