#!/usr/bin/env python3
# dump-scenario-vtypes.py
# -----------------------
# NETWORK-side, SUMO-required helper (NOT part of the offline dotnet test loop).
#
# Emits the *empirically resolved* parameters of EVERY vType used by a scenario's
# vehicles, keyed by vType id. Complements dump-vtype-defaults.py (which dumps only
# the pure default passenger type): this loads a real scenario config, steps until
# each vehicle is inserted, and reads back the fully-resolved type params via the
# typed vehicletype getters -- so it captures per-vType overrides (e.g. a leader's
# maxSpeed) exactly as SUMO resolves them, on top of the implicit vClass defaults
# that --save-state does not expand.
#
# Usage:
#   python3 scripts/dump-scenario-vtypes.py <config.sumocfg> [output.json]
# Default output: <scenario dir>/golden.vtype.json
#
# Requires: SUMO installed (scripts/install-sumo.sh) with libsumo or traci importable.

import glob
import json
import os
import subprocess
import sys

REPO_ROOT = subprocess.check_output(
    ["git", "rev-parse", "--show-toplevel"], text=True
).strip()


def _add_sumo_tools_to_path():
    candidates = []
    if os.environ.get("SUMO_HOME"):
        candidates.append(os.path.join(os.environ["SUMO_HOME"], "tools"))
    for base in sys.path + ["/usr/local/lib", "/usr/lib"]:
        candidates += glob.glob(os.path.join(base, "python*", "dist-packages", "sumo", "tools"))
    for tools in candidates:
        if os.path.isdir(os.path.join(tools, "traci")):
            if tools not in sys.path:
                sys.path.insert(0, tools)
            os.environ.setdefault("SUMO_HOME", os.path.dirname(tools))
            return tools
    return None


def sumo_version():
    try:
        return subprocess.check_output(["sumo", "--version"], text=True).splitlines()[0].strip()
    except Exception as e:  # noqa: BLE001
        return "unknown ({})".format(e)


def resolve_type(vt, tid):
    return {
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


def main():
    if len(sys.argv) < 2:
        sys.stderr.write("usage: dump-scenario-vtypes.py <config.sumocfg> [output.json]\n")
        sys.exit(2)
    cfg = os.path.abspath(sys.argv[1])
    out_path = sys.argv[2] if len(sys.argv) > 2 else os.path.join(os.path.dirname(cfg), "golden.vtype.json")

    _add_sumo_tools_to_path()
    try:
        import libsumo as conn
        backend = "libsumo"
    except ImportError:
        try:
            import traci as conn
            backend = "traci"
        except ImportError as e:
            sys.stderr.write("ERROR: neither libsumo nor traci importable ({}).\n".format(e))
            sys.exit(1)

    conn.start(["sumo", "-c", cfg, "--no-step-log", "true"])

    # Step until every vehicle in the scenario has been inserted, so every vType is
    # instantiated and fully resolved. Bounded by the scenario end to avoid hanging.
    seen_types = {}
    for _ in range(100000):
        conn.simulationStep()
        for vid in conn.vehicle.getIDList():
            tid = conn.vehicle.getTypeID(vid)
            if tid not in seen_types:
                seen_types[tid] = resolve_type(conn.vehicletype, tid)
        if conn.simulation.getMinExpectedNumber() <= 0:
            break
    conn.close()

    doc = {
        "_comment": (
            "Empirically-resolved SUMO vType parameters for every vType used by this "
            "scenario, dumped via libsumo/TraCI. Ground truth for the vType init "
            "cross-check; captures per-vType overrides on top of implicit vClass defaults."
        ),
        "sumo_version": sumo_version(),
        "backend": backend,
        "vTypes": dict(sorted(seen_types.items())),
    }
    with open(out_path, "w") as f:
        json.dump(doc, f, indent=2, sort_keys=True)
        f.write("\n")

    print("Wrote {}".format(out_path))
    print(json.dumps(doc["vTypes"], indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
