#!/usr/bin/env python3
# dump-vtype-defaults.py
# ----------------------
# NETWORK-side, SUMO-required helper (NOT part of the offline dotnet test loop).
#
# Purpose: emit the *empirically resolved* default parameters of SUMO's default
# passenger vType — i.e. what SUMO actually computes after applying every implicit
# vClass default. This is the ground-truth companion to the offline source-read of
# the vClass defaults table, because `--save-state` does NOT expand implicit vType
# defaults (it echoes only attributes explicitly set in rou.xml).
#
# Method: build a throwaway minimal scenario whose single <vType> declares only
# vClass="passenger" (NO other overrides — so every value below is a pure default),
# load it via libsumo, insert one vehicle, step once, then read the resolved type
# parameters back through the typed vehicletype getters. Emit to a JSON file.
#
# Deliberately does NOT set --default.speeddev or sigma, so speedFactor deviation
# and sigma keep their pristine vClass defaults (0.1 and 0.5). The rung-1 scenario
# separately overrides sigma=0 and speeddev=0 for determinism; that override lives
# in the scenario's own rou/cfg, not here.
#
# Usage:
#   python3 scripts/dump-vtype-defaults.py [output.json]
# Default output: scenarios/01-single-free-flow/golden.vtype.json
#
# Requires: SUMO 1.20.0 installed (scripts/install-sumo.sh) with libsumo importable.

import glob
import json
import os
import subprocess
import sys
import tempfile

REPO_ROOT = subprocess.check_output(
    ["git", "rev-parse", "--show-toplevel"], text=True
).strip()


def _add_sumo_tools_to_path():
    """Make traci/sumolib importable regardless of how SUMO was installed.

    The eclipse-sumo pip wheel ships the Python API under <pkg>/sumo/tools but
    does not put it on sys.path. Honor SUMO_HOME if set, else discover the wheel.
    """
    candidates = []
    if os.environ.get("SUMO_HOME"):
        candidates.append(os.path.join(os.environ["SUMO_HOME"], "tools"))
    for base in sys.path + ["/usr/local/lib", "/usr/lib"]:
        candidates += glob.glob(os.path.join(base, "**", "sumo", "tools"), recursive=False)
        candidates += glob.glob(os.path.join(base, "python*", "dist-packages", "sumo", "tools"))
    for tools in candidates:
        if os.path.isdir(os.path.join(tools, "traci")):
            if tools not in sys.path:
                sys.path.insert(0, tools)
            os.environ.setdefault("SUMO_HOME", os.path.dirname(tools))
            return tools
    return None

DEFAULT_OUT = os.path.join(
    REPO_ROOT, "scenarios", "01-single-free-flow", "golden.vtype.json"
)

# Reuse the committed rung-1 network so we exercise real geometry; the vType here
# is a fresh default passenger type, independent of the scenario's rou.rou.xml.
NET = os.path.join(REPO_ROOT, "scenarios", "01-single-free-flow", "net.net.xml")

ROUTE_XML = """<?xml version="1.0" encoding="UTF-8"?>
<routes>
    <vType id="defPassenger" vClass="passenger"/>
    <route id="r0" edges="e0"/>
    <vehicle id="probe" type="defPassenger" route="r0" depart="0" departPos="0" departSpeed="0" departLane="0"/>
</routes>
"""


def sumo_version():
    try:
        out = subprocess.check_output(["sumo", "--version"], text=True)
        return out.splitlines()[0].strip()
    except Exception as e:  # noqa: BLE001
        return "unknown ({})".format(e)


def main():
    out_path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT

    _add_sumo_tools_to_path()
    # Prefer libsumo (in-process, no socket); fall back to traci (subprocess).
    # Both expose the same vehicle / vehicletype getter API used below.
    try:
        import libsumo as conn
        backend = "libsumo"
    except ImportError:
        try:
            import traci as conn
            backend = "traci"
        except ImportError as e:
            sys.stderr.write(
                "ERROR: neither libsumo nor traci importable ({}). "
                "Run scripts/install-sumo.sh first.\n".format(e)
            )
            sys.exit(1)

    with tempfile.TemporaryDirectory() as tmp:
        rou_path = os.path.join(tmp, "probe.rou.xml")
        with open(rou_path, "w") as f:
            f.write(ROUTE_XML)

        # Minimal, deterministic load. No sigma / speeddev overrides -> pure defaults.
        conn.start([
            "sumo",
            "-n", NET,
            "-r", rou_path,
            "--step-length", "1",
            "--begin", "0",
            "--no-step-log", "true",
            "--seed", "42",
        ])

        # Step until the probe vehicle is inserted so its type is fully resolved.
        vid = "probe"
        for _ in range(5):
            conn.simulationStep()
            if vid in conn.vehicle.getIDList():
                break
        else:
            conn.close()
            sys.stderr.write("ERROR: probe vehicle never inserted.\n")
            sys.exit(1)

        tid = conn.vehicle.getTypeID(vid)
        vt = conn.vehicletype

        resolved = {
            "vClass": vt.getVehicleClass(tid),
            "length": vt.getLength(tid),
            "minGap": vt.getMinGap(tid),
            "maxSpeed": vt.getMaxSpeed(tid),
            "accel": vt.getAccel(tid),
            "decel": vt.getDecel(tid),
            "emergencyDecel": vt.getEmergencyDecel(tid),
            "apparentDecel": vt.getApparentDecel(tid),
            "sigma": vt.getImperfection(tid),
            "tau": vt.getTau(tid),
            "speedFactor": vt.getSpeedFactor(tid),
            "speedDeviation": vt.getSpeedDeviation(tid),
            "width": vt.getWidth(tid),
            "height": vt.getHeight(tid),
            "carFollowModel": "Krauss",
        }

        conn.close()

    doc = {
        "_comment": (
            "Empirically-resolved SUMO default passenger vType parameters, dumped via "
            "libsumo/TraCI. Ground truth for the vType init cross-check. vType declared only "
            "vClass='passenger' (no overrides), so every value is a pure vClass default. "
            "NOTE: the rung-1 scenario overrides sigma=0 and speeddev=0 for determinism; "
            "those overrides are in the scenario rou/cfg, not reflected here."
        ),
        "sumo_version": sumo_version(),
        "backend": backend,
        "vType": resolved,
    }

    with open(out_path, "w") as f:
        json.dump(doc, f, indent=2, sort_keys=True)
        f.write("\n")

    print("Wrote {}".format(out_path))
    print(json.dumps(resolved, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
