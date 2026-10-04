"""Flies one run of the performance protocol, with PQS Bench or with KSPProfiler installed.

It drives KSP through KSP-MCPServer, a mod that answers HTTP requests on 127.0.0.1, and needs nothing but
Python 3: no AI, no package to install. Start a fresh KSP with KSP-MCPServer and one instrument installed
(PQS Bench, or the fork of KSPProfiler that KSP-MCPServer can drive, never both), copy ref-mune-5km.sfs into
a sandbox game, wait for the main menu, then run:

    python run-perfs.py --folder <your sandbox game> --out out

It does what the runs of docs/measuring-a-terrain-mod.md ask a player to do, in the same order: load the save,
turn the camera (looking ahead along the orbit, the Mun's ground on the left two thirds of the screen), then at
30 s of mission time start the measure, and at 1 min 40 s stop it. With PQS Bench, Reset then Dump to KSP.log,
its window hidden; with KSPProfiler, its window open as a player has it, Start capture of 10 000 frames, Stop
capture, then Export to CSV. Nothing is asked of KSP between the start and the stop of the measure: the script
sleeps through it, for the game time left converted at the pace the game kept before the start.

On Windows it also does what a player does with the game's window: brings it in front of every other window,
the keyboard with it, and moves the mouse pointer off it (full screen, to the middle of its left edge); it stops
if the window is not in front, and checks again at the start and at the stop of the measure. Play the runs full
screen (FULLSCREEN = True in settings.cfg), and do not use the computer during a run.

It writes run.json (the mission time at both ends, the frames captured, the pace) and, with KSPProfiler,
profiler.csv into --out, then quits KSP (unless --keep-running is given). With PQS Bench, the figures are in
KSP.log, which KSP overwrites at its next start: keep it.
"""
import argparse
import ctypes
import json
import os
import sys
import time
import urllib.request

URL = None
HERE = os.path.dirname(os.path.abspath(__file__))

# The capture of KSPProfiler is asked for as many frames as its window accepts: it ends on Stop, not before.
PROFILER_FRAMES = 10000


def call(tool, **args):
    """Calls one tool of KSP-MCPServer and returns its answer, decoded from JSON when it is JSON."""
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                       "params": {"name": tool, "arguments": args}}).encode()
    request = urllib.request.Request(URL, body, {"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=900) as response:
        result = json.loads(response.read())["result"]
    text = result["content"][0].get("text", "") if result["content"] else ""
    if result.get("isError"):
        raise RuntimeError(tool + ": " + text)
    try:
        answer = json.loads(text)
    except ValueError:
        return text
    # The tools another mod adds answer inside "returned".
    if isinstance(answer, dict) and list(answer) == ["returned"]:
        return answer["returned"]
    return answer


def instrument():
    """The instrument installed, "pqsbench" or "profiler"; stops when there is none, or both."""
    body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/list"}).encode()
    request = urllib.request.Request(URL, body, {"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=60) as response:
        names = {t["name"] for t in json.loads(response.read())["result"]["tools"]}
    found = [i for i, tool in (("pqsbench", "pqsbench_reset"), ("profiler", "profiler_start_capture"))
             if tool in names]
    if len(found) != 1:
        raise SystemExit("error: install PQS Bench or KSPProfiler, one of them: found " + (" and ".join(found) or
                                                                                         "neither"))
    return found[0]


def launch_time(path):
    """The launch time (lct) of the active vessel of a save, which mission time counts from."""
    # The names of the nodes the current line is in, GAME/FLIGHTSTATE/VESSEL for a vessel's own values.
    path_names = []
    name = None
    active = None
    vessels = []
    with open(path, encoding="utf-8-sig") as f:
        for line in f:
            text = line.strip()
            if text == "{":
                path_names.append(name)
                if path_names[-2:] == ["FLIGHTSTATE", "VESSEL"]:
                    vessels.append(None)
            elif text == "}":
                path_names.pop()
            elif "=" not in text:
                name = text
            elif path_names[-1:] == ["FLIGHTSTATE"] and text.startswith("activeVessel ="):
                active = int(text.split("=", 1)[1])
            elif path_names[-2:] == ["FLIGHTSTATE", "VESSEL"] and text.startswith("lct ="):
                vessels[-1] = float(text.split("=", 1)[1])
    if active is None or active >= len(vessels) or vessels[active] is None:
        raise SystemExit("error: no active vessel with a launch time in " + path)
    return vessels[active]


def in_front(title):
    """Whether the window of that title is the foreground window; None but on Windows."""
    if sys.platform != "win32":
        return None
    user32 = ctypes.windll.user32
    window = user32.FindWindowW(None, title)
    return bool(window) and user32.GetForegroundWindow() == window


def bring_to_front(title):
    """Makes the window of that title the foreground window, in front of every other one and with the keyboard,
    and moves the mouse pointer off it. Returns whether it is in front; None but on Windows."""
    if sys.platform != "win32":
        return None
    user32 = ctypes.windll.user32
    window = user32.FindWindowW(None, title)
    if not window:
        return False
    # Windows lets a program take the foreground only in some cases, one of them right after a key press:
    # Alt pressed and released, then the window asked for. Raising it without the keyboard is not enough,
    # Windows leaves it behind the window in use.
    user32.keybd_event(0x12, 0, 0, 0)  # VK_MENU down
    user32.keybd_event(0x12, 0, 2, 0)  # VK_MENU up
    user32.SetForegroundWindow(window)
    time.sleep(0.5)

    class Rect(ctypes.Structure):
        _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long), ("right", ctypes.c_long),
                    ("bottom", ctypes.c_long)]
    rect = Rect()
    user32.GetWindowRect(window, ctypes.byref(rect))
    width, height = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
    # The middle of the first edge of the screen outside the window, never a corner: the bottom right one is
    # Windows' "show desktop" button, whose hover makes every window transparent. Full screen, the middle of
    # its left edge, over the sky or the ground, away from the craft.
    for x, y in ((2, height // 2), (width - 3, height // 2), (width // 2, 2)):
        if not (rect.left <= x < rect.right and rect.top <= y < rect.bottom):
            user32.SetCursorPos(x, y)
            break
    else:
        user32.SetCursorPos(2, height // 2)
    return in_front(title)


def log(*parts):
    print(time.strftime("%H:%M:%S"), *parts, flush=True)


def main():
    global URL
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--folder", required=True, help="the sandbox game under saves/ the save was copied into")
    parser.add_argument("--save", default="ref-mune-5km", help="the save, without .sfs")
    parser.add_argument("--sfs", help="a copy of the save, read for its launch time (default: ../<save>.sfs)")
    parser.add_argument("--start", type=float, default=30, help="mission time the measure starts at, seconds")
    parser.add_argument("--stop", type=float, default=100, help="mission time the measure stops at, seconds")
    parser.add_argument("--heading", type=float, default=204,
                        help="camera heading, degrees: in orbit, the camera's frame is the orbit's, and 204 looks "
                             "ahead with the ground on the left two thirds of the screen")
    parser.add_argument("--out", default="out", help="where run.json and profiler.csv go")
    parser.add_argument("--port", type=int, default=8770, help="the port of KSP-MCPServer")
    parser.add_argument("--keep-running", action="store_true", help="leave KSP running at the end")
    parser.add_argument("--window-title", default="Kerbal Space Program", help="the title of KSP's window")
    parser.add_argument("--leave-behind", action="store_true",
                        help="leave KSP's window where it is, behind others or not (a control run, not the protocol)")
    parser.add_argument("--profiler-window", type=float, nargs=2, metavar=("X", "Y"),
                        help="move KSPProfiler's window this far from the screen's centre, in the units of the "
                             "game's interface (a control run, not the protocol: it opens in the middle)")
    options = parser.parse_args()
    URL = "http://127.0.0.1:%d/mcp/" % options.port
    out = os.path.abspath(options.out)
    os.makedirs(out, exist_ok=True)
    launched = launch_time(options.sfs or os.path.join(os.path.dirname(HERE), options.save + ".sfs"))
    measuring = instrument()
    log("instrument:", measuring, "- launch time", launched)

    call("load_save", folder=options.folder, save=options.save)
    state = call("get_state")
    log("loaded at mission time %.2f s" % (state["ut"] - launched))
    if state["ut"] - launched > options.start - 10:
        raise SystemExit("error: the load ended too late to frame the camera before the measure")

    # What a player does right after the load: the camera turned to its place, the windows as a run has them.
    camera = call("set_camera", heading=options.heading, pitch=0)
    if measuring == "pqsbench":
        call("pqsbench_show_window", visible=False)
    else:
        call("profiler_open")
        if options.profiler_window:
            log("profiler window:", call("profiler_move_window", x=options.profiler_window[0],
                                         y=options.profiler_window[1]))
    front = None if options.leave_behind else bring_to_front(options.window_title)
    log("KSP's window in front:", front)
    if front is False:
        raise SystemExit("error: KSP's window could not be brought in front of the others")

    # Up to the start, the game is read as often as needed; the pace of game time against real time it kept
    # meanwhile converts what is left of the measure into a sleep.
    first_real, first_ut = time.time(), call("get_state")["ut"]
    while True:
        real, ut = time.time(), call("get_state")["ut"]
        if ut - launched >= options.start - 0.05:
            front_at_start = in_front(options.window_title)
            break
        time.sleep(0.05)
    pace = (ut - first_ut) / (real - first_real)
    if measuring == "pqsbench":
        call("pqsbench_reset")
    else:
        call("profiler_start_capture", frames=PROFILER_FRAMES)
    started_real = time.time()
    started = ut - launched + (started_real - real) * pace
    time.sleep(max(0.0, (options.stop - started) / pace - (time.time() - started_real)))

    # The stop, and only then anything else.
    if measuring == "pqsbench":
        call("pqsbench_dump")
    else:
        call("profiler_stop_capture")
    stopped_real = time.time()
    front_at_stop = in_front(options.window_title)
    stopped = call("get_state")["ut"] - launched
    result = {"instrument": measuring, "save": options.save, "launchTime": launched,
              "missionTimeStart": round(started, 3), "missionTimeStop": round(stopped, 3),
              "gameSeconds": round(stopped - started, 3), "realSeconds": round(stopped_real - started_real, 3),
              "paceBefore": round(pace, 4), "camera": camera, "windowInFront": front,
              "windowInFrontAtStart": front_at_start, "windowInFrontAtStop": front_at_stop,
              "profilerWindow": options.profiler_window}
    if measuring == "profiler":
        state = call("profiler_state")
        result["profiler"] = state
        result["csv"] = call("profiler_export", directory=out, fileName="profiler.csv")
    with open(os.path.join(out, "run.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(result, f, indent=1)
    log(json.dumps(result))
    if not options.keep_running:
        call("quit_game")


if __name__ == "__main__":
    main()
