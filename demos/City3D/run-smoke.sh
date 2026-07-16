#!/usr/bin/env bash
# City3D demo — headless smoke test for the Viewer project skeleton (task T1.1).
#
# Packs the local feed, builds the Godot C# assembly, resolves the Godot 4 (.NET/mono) engine binary via
# fetch-godot.sh, and runs the Viewer project headless for a capped number of frames. PASS means the
# scripted Main.cs heartbeat ran to completion (non-zero exit-triggering errors are treated as FAIL);
# this does not (and cannot, headless) confirm anything about rendered pixels -- see
# docs/DEMO-CITY3D-DESIGN.md "What is verified where".
#
# Usage:
#   demos/City3D/run-smoke.sh
#
# Requires: .NET 8 SDK on PATH; network access (nuget.org for Godot.NET.Sdk, downloads.godotengine.org for
# the engine binary -- both ephemeral, never committed).
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
DEMO="$ROOT/demos/City3D"
VIEWER="$DEMO/Viewer"

echo "==> [1/4] packing the local NuGet feed"
bash "$DEMO/build.sh" --pack-only

echo "==> [2/4] building the Viewer Godot C# assembly (Debug -- what the headless editor/runtime loads)"
dotnet build "$VIEWER" -c Debug

echo "==> [3/4] resolving the Godot 4 (.NET/mono) engine binary"
GODOT_BIN="$("$DEMO/fetch-godot.sh" | tail -1)"
echo "    godot binary: $GODOT_BIN"

echo "==> [4/4] running the Viewer headless"
# --fixed-fps pins _Process's `delta` to a fixed 1/60s per frame: under --headless with the dummy
# renderer, real wall-clock deltas between frames are far smaller and far less regular than real-time
# (frames run "as fast as possible"), so without a fixed step the sim-cadence accumulator in Main.cs would
# need hundreds of real frames to accumulate one simulated second. --quit-after is a generous safety net;
# Main.cs itself calls GetTree().Quit() first, after its own (smaller) frame cap.
LOG="$(mktemp)"
trap 'rm -f "$LOG"' EXIT

set +e
"$GODOT_BIN" --headless --path "$VIEWER" --fixed-fps 60 --quit-after 400 > "$LOG" 2>&1
STATUS=$?
set -e

cat "$LOG"

if [[ $STATUS -ne 0 ]]; then
  echo "FAIL: godot exited with status $STATUS"
  exit 1
fi

if grep -q '^ERROR:' "$LOG"; then
  echo "FAIL: godot reported an ERROR"
  exit 1
fi

if ! grep -q 'Main: reached .* frames, quitting\.' "$LOG"; then
  echo "FAIL: Main.cs heartbeat never reached its frame cap"
  exit 1
fi

if ! grep -qE 'Main: frame=[0-9]+ simTime=[0-9.]+ vehicles=[1-9]' "$LOG"; then
  echo "FAIL: no heartbeat line showed a non-zero vehicle count"
  exit 1
fi

echo "PASS: headless Viewer smoke run OK"
