using System;
using System.IO;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>How much the mod writes to KSP.log, from the quietest to the most talkative.</summary>
    internal enum LogLevel
    {
        Error,
        Warning,
        Info,
        Debug,
        Trace
    }

    /// <summary>
    /// Writes to KSP.log, tagged with the mod's name, filtered by the level set in
    /// PluginData/settings.cfg. The level is read once, when KSP starts.
    /// </summary>
    internal static class Log
    {
        private const string Prefix = "[PQSBench] ";

        private static LogLevel _level = LogLevel.Info;

        public static LogLevel Level => _level;
        public static bool IsDebugEnabled => _level >= LogLevel.Debug;
        public static bool IsTraceEnabled => _level >= LogLevel.Trace;

        /// <summary>
        /// Reads the level from PluginData/settings.cfg, next to the DLL. A missing file or an unknown
        /// value leaves it at Info.
        /// </summary>
        public static void LoadLevel()
        {
            string folder = Path.GetDirectoryName(typeof(Log).Assembly.Location);
            string path = Path.Combine(Path.Combine(folder, "PluginData"), "settings.cfg");
            if (!File.Exists(path))
            {
                Warning($"No settings file at {path}, logging at {_level}");
                return;
            }

            string value = ConfigNode.Load(path)?.GetValue("logLevel");
            LogLevel parsed;
            if (Enum.TryParse(value, true, out parsed) && Enum.IsDefined(typeof(LogLevel), parsed))
            {
                _level = parsed;
            }
            else
            {
                Warning($"Unknown logLevel '{value}' in {path}, logging at {_level}");
            }
        }

        public static void Error(string message)
        {
            UnityEngine.Debug.LogError(Prefix + message);
        }

        public static void Warning(string message)
        {
            if (_level >= LogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning(Prefix + message);
            }
        }

        public static void Info(string message)
        {
            if (_level >= LogLevel.Info)
            {
                UnityEngine.Debug.Log(Prefix + message);
            }
        }

        public static void Debug(string message)
        {
            if (_level >= LogLevel.Debug)
            {
                UnityEngine.Debug.Log(Prefix + "[DEBUG] " + message);
            }
        }

        public static void Trace(string message)
        {
            if (_level >= LogLevel.Trace)
            {
                UnityEngine.Debug.Log(Prefix + "[TRACE] " + message);
            }
        }
    }
}
