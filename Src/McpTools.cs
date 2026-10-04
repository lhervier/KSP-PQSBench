using System.Collections.Generic;
using UnityEngine;
using com.github.lhervier.ksp.mcpserver;

namespace com.github.lhervier.ksp.pqsbench
{
    /// <summary>
    /// What KSP-MCPServer, when it is installed, offers of this mod as tools: its two buttons and the moving
    /// of its window. Nothing here is needed to measure by hand, and this mod runs the same without
    /// KSP-MCPServer: only that server reads the attribute.
    /// </summary>
    internal static class McpTools
    {
        [McpTool("pqsbench_dump",
            "Presses Dump in the window of PQS Bench: writes everything recorded since the game started, or " +
                "since the last Reset, to KSP.log, between a BENCH begin and a BENCH end line.")]
        internal static void Dump()
        {
            PQSBenchMod.Dump();
        }

        [McpTool("pqsbench_reset", "Presses Reset in the window of PQS Bench: throws away everything recorded.")]
        internal static void Reset()
        {
            PQSBenchMod.Reset();
        }

        [McpTool("pqsbench_move_window",
            "Moves the window of PQS Bench, as dragging it does: x and y in pixels from the top left corner " +
            "of the screen. Returns its position and size (x, y, width, height).")]
        internal static object MoveWindow(double x, double y)
        {
            Rect rect = PQSBenchMod.WindowRect;
            rect.x = (float)x;
            rect.y = (float)y;
            PQSBenchMod.WindowRect = rect;
            return new Dictionary<string, object>
            {
                { "x", (double)rect.x },
                { "y", (double)rect.y },
                { "width", (double)rect.width },
                { "height", (double)rect.height }
            };
        }

        [McpTool("pqsbench_show_window",
            "Shows or hides the window of PQS Bench, as Mod+F6 does; what it measures goes on either " +
            "way. It only shows in flight, while recording. Returns whether it shows (visible).")]
        internal static object ShowWindow(bool visible)
        {
            PQSBenchMod.WindowVisible = visible;
            return new Dictionary<string, object> { { "visible", PQSBenchMod.WindowVisible } };
        }
    }
}
