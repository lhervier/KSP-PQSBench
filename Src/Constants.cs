using UnityEngine;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>Every constant of the host: the mod, its settings, its log and its keys.</summary>
    internal static class Constants
    {
        // ---- The mod ----

        internal const string HarmonyId = "com.github.lhervier.ksp.pqsbench";

        // ---- Settings ----

        // PluginData/settings.cfg, next to the DLL. Read once, when KSP starts.
        internal const string SettingsFolder = "PluginData";
        internal const string SettingsFile = "settings.cfg";
        internal const string LogLevelSetting = "logLevel";

        // ---- Log ----

        internal const string LogPrefix = "[PQSBench] ";

        // ---- Keys, pressed along with the modifier (Alt) ----

        internal const KeyCode DumpKey = KeyCode.F8;
        internal const KeyCode ResetKey = KeyCode.F7;
    }
}
