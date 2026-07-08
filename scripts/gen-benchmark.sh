#!/usr/bin/env bash
#
# gen-benchmark.sh <targetConcurrency>
# -------------------------------------
# VB-5 (VIZ_BENCH_TASKS.md Phase 2 / BENCHMARK_SPEC.md): generates the net + routes + config for
# one rung of the scaled-city benchmark, from the pinned pip SUMO install ALONE (no external
# scenario download) -- netgenerate for the network, randomTrips.py + duarouter for demand. The
# rung is a single argument: target PEAK CONCURRENT vehicles (not total trips). Everything else is
# derived: the insertion `period` is tuned via Little's law (concurrent ~= insertion_rate *
# mean_trip_time) against a short pilot SUMO run, then refined 1-2 more times by actually
# measuring peak/mean `running` from --summary-output, per BENCHMARK_SPEC.md's tuning heuristic.
#
# This is a [net] step: NETWORK-ENABLED VM, deliberate, ends in a commit of the small/aggregate
# outputs (net/rou/cfg/provenance -- see VB-8 for which outputs are committed vs regenerated).
# NOT part of `dotnet test` -- SUMO is never required for the offline parity loop.
#
# Usage:
#   scripts/gen-benchmark.sh <targetConcurrency>
#   scripts/gen-benchmark.sh 30
#
# Output: scenarios/_bench/city-<targetConcurrency>/{net.net.xml,rou.rou.xml,config.sumocfg,
#         provenance.txt}, plus a pilot summary/tripinfo pair left in the work dir for inspection
#         (VB-8 re-runs the FINAL SUMO reference pass itself and commits ITS summary/tripinfo).
#
# ============================================================================================
# ENGINE CAPABILITY FINDING (read before changing LANES below):
#
# netgenerate's default multi-lane connection assignment (-L 2+) does not give every lane a
# <connection> to every route-reachable next edge (e.g. a straight-only lane vs a turn-only
# lane). SUMO itself resolves this via *dynamic* strategic lane-changing while a vehicle
# travels. The engine's ported strategic lane-change (C2-ii, see Sim.Core/Engine.cs
# TryStrategicLaneChange) is explicitly a "single-look-ahead scoped port" -- it only resolves
# the FIRST edge transition of a route at insertion (NetworkModel.ResolveLaneSequence /
# ComputeBestLanes) and only handles a same-edge drop-lane convergence at runtime, not a full
# multi-hop strategic replan. A multi-edge route through a multi-lane grid net WILL throw
# "No <connection> found from edge '...' lane N to edge '...'" at insertion whenever the
# lane the greedy walk lands on doesn't happen to continue.
#
# This was reproduced directly: a 3x3 grid, -L 2, --tls.guess, randomTrips --fringe-factor 5
# demand reliably hits this on ~1/200 vehicles. The SAME net/demand pipeline at -L 1 (single
# lane per edge -- every lane trivially has exactly one outgoing connection per direction, so
# route-to-lane resolution can never be ambiguous) runs 600 simulated seconds / 200 vehicles to
# completion with zero errors. LANES is therefore pinned at 1 for now -- this is the
# "simplify the generated net/demand until the engine runs it" scoping BENCHMARK bring-up calls
# for, not a Sim.Core change. Bumping LANES back up (to actually exercise lane-changing/
# overtaking, as BENCHMARK_SPEC.md's network-generation section wants) is future engine work:
# extend ComputeBestLanes/ResolveLaneSequence to a real multi-hop lookahead (or dynamically
# re-resolve the lane sequence at each junction instead of once at insertion).
# ============================================================================================
#
# 15k-RUNG NOTE (also read before scaling up): BENCHMARK_SPEC.md says to size the net ONCE at the
# 15k rung so the same net serves every rung (only demand/period changes). This script currently
# regenerates a FIXED small net every call (sized for the ~30..~300 bring-up rungs) -- that is
# intentional for now (tune the pipeline small first, per the spec's own ladder). Scaling to
# thousands/15k concurrent on a single-lane net means a much longer/larger net (more lane-km) is
# needed to avoid artificial gridlock at high density; that requires either a bigger --grid.number
# or switching to `netgenerate --rand` with a controlled node count, sized once by measuring
# density at the 15k rung, and is left as a follow-up (VB-8's own per-rung task) rather than done
# here.

set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: $0 <targetConcurrency>" >&2
  exit 2
fi

TARGET_CONCURRENCY="$1"
if ! [[ "$TARGET_CONCURRENCY" =~ ^[0-9]+$ ]] || [[ "$TARGET_CONCURRENCY" -le 0 ]]; then
  echo "ERROR: targetConcurrency must be a positive integer, got '$TARGET_CONCURRENCY'" >&2
  exit 2
fi

REPO_ROOT="$(git rev-parse --show-toplevel)"
cd "$REPO_ROOT"

# shellcheck disable=SC1091
source "$REPO_ROOT/SUMO_VERSION"
: "${SUMO_VERSION:?Set SUMO_VERSION in $REPO_ROOT/SUMO_VERSION}"

: "${SUMO_HOME:=/usr/local/lib/python3.11/dist-packages/sumo}"
export SUMO_HOME
if [[ ! -d "$SUMO_HOME" ]]; then
  echo "ERROR: SUMO_HOME ($SUMO_HOME) not found -- run scripts/install-sumo.sh first." >&2
  exit 1
fi
if ! command -v sumo >/dev/null 2>&1 || ! command -v netgenerate >/dev/null 2>&1 \
    || ! command -v duarouter >/dev/null 2>&1; then
  echo "ERROR: sumo/netgenerate/duarouter not on PATH -- run scripts/install-sumo.sh first." >&2
  exit 1
fi

OUT_DIR="$REPO_ROOT/scenarios/_bench/city-${TARGET_CONCURRENCY}"
mkdir -p "$OUT_DIR"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cd "$WORK"

# ---- fixed knobs (bring-up sizing; see the 15k-rung note above) ----------------------------
SEED=42
END=600            # simulated seconds (steady-state plateau window)
GRID_NUMBER=3      # 3x3 grid -> 9 junctions (mix of priority + TLS via --tls.guess)
GRID_LENGTH=200    # meters per grid edge
LANES=1            # see ENGINE CAPABILITY FINDING above -- do not bump without re-validating
                    # multi-edge routes against the engine first (dotnet run --project src/Sim.Run)
FRINGE_FACTOR=5

hash_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$1" | awk '{print $1}'
  else
    shasum -a 256 "$1" | awk '{print $1}'
  fi
}

# ---- 1. network (shared by every tuning iteration below) -----------------------------------
NETGEN_CMD=(netgenerate --grid --grid.number="$GRID_NUMBER" --grid.length="$GRID_LENGTH" \
  -L "$LANES" --tls.guess --seed "$SEED" -o city.net.xml)
echo "==> ${NETGEN_CMD[*]}"
"${NETGEN_CMD[@]}"

write_config() {
  local cfg="$1"
  local net_file="${2:-city.net.xml}"
  local route_file="${3:-city.rou.xml}"
  cat > "$cfg" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<configuration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:noNamespaceSchemaLocation="http://sumo.dlr.de/xsd/sumoConfiguration.xsd">
    <input>
        <net-file value="${net_file}"/>
        <route-files value="${route_file}"/>
    </input>
    <time>
        <begin value="0"/>
        <end value="${END}"/>
        <step-length value="1"/>
    </time>
    <processing>
        <step-method.ballistic value="false"/>
        <time-to-teleport value="-1"/>
        <default.action-step-length value="1"/>
        <default.speeddev value="0"/>
    </processing>
    <random_number>
        <seed value="${SEED}"/>
    </random_number>
</configuration>
EOF
}

# Adds an explicit DEFAULT_VEHTYPE (sigma=0 -- phase-1 determinism convention, see CLAUDE.md
# "Determinism (phase 1)") and references it from every <vehicle>, since randomTrips.py/duarouter
# omit a <vType> and the engine's DemandParser (unlike SUMO) does not synthesize an implicit
# default vType -- see gen-benchmark.sh's own header note if this throws KeyNotFoundException.
add_default_vtype() {
  local rou="$1"
  python3 - "$rou" <<'PYEOF'
import re
import sys

path = sys.argv[1]
with open(path) as f:
    content = f.read()

content = re.sub(
    r'(<routes[^>]*>)',
    r'\1\n    <vType id="DEFAULT_VEHTYPE" vClass="passenger" sigma="0"/>',
    content, count=1)
content = re.sub(
    r'<vehicle id="([^"]+)" depart=',
    r'<vehicle id="\1" type="DEFAULT_VEHTYPE" depart=',
    content)

with open(path, 'w') as f:
    f.write(content)
PYEOF
}

# Generates demand at a given insertion `period`, pre-routes with duarouter --named-routes (the
# format DemandParser needs: <route id=.../> + <vehicle route="id"/>, NOT duarouter's default
# embedded <route> child -- see gen-benchmark.sh's ENGINE CAPABILITY FINDING section for the other
# format gap this avoids), and patches in DEFAULT_VEHTYPE. Writes city.rou.xml in $WORK.
generate_demand() {
  local period="$1"
  python3 "$SUMO_HOME/tools/randomTrips.py" -n city.net.xml -e "$END" -p "$period" \
    --fringe-factor "$FRINGE_FACTOR" --seed "$SEED" -o city.trips.xml -r city.trips.rou.xml \
    >/dev/null
  duarouter -n city.net.xml -r city.trips.xml -o city.rou.xml --seed "$SEED" \
    --ignore-errors --named-routes >/dev/null
  add_default_vtype city.rou.xml
}

# Runs SUMO on the CURRENT city.net.xml/city.rou.xml, returns (via globals) the measured mean
# trip duration and peak/mean-steady running count from --summary-output.
measure_concurrency() {
  write_config city.sumocfg
  sumo -c city.sumocfg --summary-output measure.summary.xml --tripinfo-output measure.tripinfo.xml \
    --no-step-log true >/dev/null
  read -r MEAN_TRIP_TIME PEAK_RUNNING MEAN_RUNNING_STEADY N_ARRIVED < <(python3 - <<'PYEOF'
import xml.etree.ElementTree as ET

summary = ET.parse("measure.summary.xml").getroot()
steps = summary.findall("step")
running = [int(s.get("running")) for s in steps]
peak = max(running) if running else 0
tail = running[len(running)//3:] if running else []
mean_steady = (sum(tail) / len(tail)) if tail else 0.0

trips = ET.parse("measure.tripinfo.xml").getroot()
durations = [float(t.get("duration")) for t in trips.findall("tripinfo")]
mean_duration = (sum(durations) / len(durations)) if durations else 0.0

print(f"{mean_duration:.6f} {peak} {mean_steady:.6f} {len(durations)}")
PYEOF
)
}

# ---- 2. pilot run: rough period, just to estimate mean trip time ---------------------------
PILOT_PERIOD="5.0"
echo "==> pilot demand at period=${PILOT_PERIOD}s (estimating mean trip time)"
generate_demand "$PILOT_PERIOD"
measure_concurrency
echo "    pilot: mean_trip_time=${MEAN_TRIP_TIME}s peak_running=${PEAK_RUNNING} mean_running=${MEAN_RUNNING_STEADY} arrived=${N_ARRIVED}"

# ---- 3. Little's law: period ~= mean_trip_time / target, then measure + refine up to 2x ----
PERIOD=$(python3 -c "print(max(0.1, ${MEAN_TRIP_TIME} / ${TARGET_CONCURRENCY}))")
for ITER in 1 2; do
  echo "==> tuning iteration ${ITER}: period=${PERIOD}s (target concurrency=${TARGET_CONCURRENCY})"
  generate_demand "$PERIOD"
  measure_concurrency
  echo "    measured: mean_trip_time=${MEAN_TRIP_TIME}s peak_running=${PEAK_RUNNING} mean_running=${MEAN_RUNNING_STEADY} arrived=${N_ARRIVED}"

  # Close enough (within ~35% of target on the steady-state mean)? Stop early.
  WITHIN_BAND=$(python3 -c "
target = ${TARGET_CONCURRENCY}
mean = ${MEAN_RUNNING_STEADY}
print(1 if target * 0.65 <= mean <= target * 1.35 else 0)
")
  if [[ "$WITHIN_BAND" == "1" ]]; then
    echo "    within target band -- stopping tuning."
    break
  fi

  # Re-derive period from the just-measured mean_trip_time and steady concurrency (Little's
  # law again, using the ACTUAL measured relationship rate=1/period -> concurrent=mean_running
  # to rescale proportionally toward the target).
  PERIOD=$(python3 -c "
period = ${PERIOD}
mean = ${MEAN_RUNNING_STEADY}
target = ${TARGET_CONCURRENCY}
print(max(0.1, period * (mean / target) if mean > 0 else period))
")
done

FINAL_PERIOD="$PERIOD"
echo "==> final tuned period=${FINAL_PERIOD}s -> peak_running=${PEAK_RUNNING} mean_running=${MEAN_RUNNING_STEADY} arrived=${N_ARRIVED}"

# ---- 4. install into the committed scenario dir ---------------------------------------------
cp city.net.xml "$OUT_DIR/net.net.xml"
cp city.rou.xml "$OUT_DIR/rou.rou.xml"
write_config "$OUT_DIR/config.sumocfg" "net.net.xml" "rou.rou.xml"

RANDOMTRIPS_CMD="python3 \$SUMO_HOME/tools/randomTrips.py -n city.net.xml -e ${END} -p ${FINAL_PERIOD} --fringe-factor ${FRINGE_FACTOR} --seed ${SEED} -o city.trips.xml -r city.trips.rou.xml"
DUAROUTER_CMD="duarouter -n city.net.xml -r city.trips.xml -o city.rou.xml --seed ${SEED} --ignore-errors --named-routes"

{
  echo "sumo_version=${SUMO_VERSION}"
  echo "sumo_version_reported=$(sumo --version 2>&1 | head -n 1)"
  echo "generated_utc=$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  echo "target_concurrency=${TARGET_CONCURRENCY}"
  echo "seed=${SEED}"
  echo "begin=0 end=${END} step-length=1"
  echo "net_command=${NETGEN_CMD[*]}"
  echo "demand_command_randomtrips=${RANDOMTRIPS_CMD}"
  echo "demand_command_duarouter=${DUAROUTER_CMD}"
  echo "demand_postprocess=add DEFAULT_VEHTYPE (vClass=passenger sigma=0) vType + type= reference on every <vehicle> (randomTrips/duarouter emit neither; DemandParser requires an explicit vType -- see gen-benchmark.sh add_default_vtype)"
  echo "tuned_insertion_period_s=${FINAL_PERIOD}"
  echo "measured_mean_trip_time_s=${MEAN_TRIP_TIME}"
  echo "measured_peak_running=${PEAK_RUNNING}"
  echo "measured_mean_running_steady=${MEAN_RUNNING_STEADY}"
  echo "measured_arrived=${N_ARRIVED}"
  echo "engine_capability_note=LANES pinned at ${LANES} -- see gen-benchmark.sh header 'ENGINE CAPABILITY FINDING' (multi-lane multi-hop route-to-lane resolution is a single-look-ahead scoped port in Sim.Core/Engine.cs, C2-ii; -L 2+ throws 'No connection found' at insertion for some multi-edge routes)"
  echo "# input file hashes (sha256):"
  for f in "$OUT_DIR"/*.net.xml "$OUT_DIR"/*.rou.xml "$OUT_DIR"/*.sumocfg; do
    [[ -e "$f" ]] || continue
    echo "input=$(basename "$f") sha256=$(hash_file "$f")"
  done
} > "$OUT_DIR/provenance.txt"

echo
echo "==> wrote $OUT_DIR/{net.net.xml,rou.rou.xml,config.sumocfg,provenance.txt}"
echo "==> next: run the SUMO reference pass + engine run + comparator + viz (see VIZ_BENCH_TASKS.md VB-7/VB-8)"
