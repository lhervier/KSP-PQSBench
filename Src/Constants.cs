using UnityEngine;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>Every constant of the host: the mod, its settings, its log and its window.</summary>
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

        // ---- The window ----

        // Pressed along with the modifier (Alt), shows or hides the window: the same key for every KSP Diag.
        internal const KeyCode WindowKey = KeyCode.F6;

        // Unique among the windows of the game.
        internal const int WindowId = 0x47485007;

        internal const float WindowX = 60f;
        internal const float WindowY = 60f;
        internal const float WindowWidth = 300f;
    }
}
