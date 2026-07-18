# ISSUE2-JUNCTION-TELEPORT-DESIGN.md — teleport yield/jam classification + the count gap

**Status:** IN PROGRESS. Issue 2, re-diagnosed by the serve-path session as a **teleport
classification / yield-wait** divergence (parking, the earlier confound, is fixed and accepted). Repro:
`scenarios/_repro/synthetic-junction/` (irregular unsignalized-priority net; the uniform 8×8 grid does
NOT show it). Branch: `claude/sumosharp-junction-row-issue2` (based on `drop-in-binary`).

## 1. The divergence (reproduced)

`--end 1000`, identical flags, only the binary differs:

| | vanilla | SumoSharp (before) | SumoSharp (after classification) |
|---|---|---|---|
| jam | 0 | 75 | **31** |
| yield | 3 | 0 | **44** |
| wrongLane | 0 | 0 | 0 |
| total | 3 | 75 | 75 |

Congestion is *equal* (sustained ≥120 s halts: vanilla 256 vs SumoSharp 205 — SumoSharp is not more
stuck), yet SumoSharp fires 25× more teleports. Two independent gaps, now separated by the classification:

- **Yield 44 vs 3** — minor-link waiters (front vehicle whose next junction link is minor, waiting for a
  right-of-way foe).
- **Jam 31 vs 0** — major-link vehicles blocked by downstream congestion. Vanilla has **zero** jam
  teleports even with impatience disabled.

## 2. SUMO's teleport logic (vendored: MSLane.cpp:2257-2300)

Only the **frontmost non-`<stop>`-stopped vehicle per lane** (`firstNotStopped`) is a candidate. It
teleports when `r1 = ttt>0 && firstNotStopped->getWaitingTime() > ttt` (consecutive wait, resets on any
movement >0.1 m/s). The jam/yield/wrongLane split is a **label** applied after r1 fires:
`wrongLane = !appropriate()`; else next link minor (`!havePriority()`) → **yield**; else → **jam**
(`havePriority() = state ∈ 'A'..'Z'`, uppercase == priority).

## 3. Part 1 — classification (LANDED, byte-identical)

Ported the MSLane.cpp:2272-2294 split into `TeleportVehicle`/`ClassifyTeleportKind` (Engine.cs): find
ego's next junction link (first internal lane on its route sequence), read its current state char (live
TL phase char for a TL link, else the static `<connection state=…>` char, now parsed into
`Connection.State`), and classify minor→yield / major→jam. Surfaced `TeleportCountYield` /
`TeleportCountWrongLane`; the drop-in `--statistic-output` now emits all three. **wrongLane** is not
produced yet (documented simplification: every in-scope scenario reports 0). Verified byte-identical:
full suite 613 green, scenario 47's golden stays `jam=1` (its teleport link is `state="M"`, major).

## 4. Part 2 — the count gap (in progress)

### 4a. Yield gap → impatience (MSLink::blockedByFoe, MSLink.cpp:921-1014)
SumoSharp's junction RoW is ported with `impatience==0` (Engine.cs:6940 "phase 1 has no impatience").
SUMO grows a vehicle's impatience with its waiting time (`getImpatience() = base + waitingTime /
gTimeToImpatience`, default `--time-to-impatience 180`) and, when `impatience>0`, assumes a
priority foe will brake for it (`foeArrivalTime = (1-imp)·foeAT + imp·foeArrivalTimeBraking`,
MSLink.cpp:949-954) — so a long-waiting minor-road vehicle forces through instead of freezing to the
120 s cutoff. **Confirmed the lever:** vanilla with `--time-to-impatience 0` rises 3→12 teleports (still
all yield, still 0 jam). Plan: port impatience into the arrival-time RoW gate, growing with
`WaitingTime`. Parity guard: impatience is 0 while `WaitingTime==0`, so scenarios whose junction waits
never accumulate stay byte-identical — to be verified against goldens 26/27/29/31/39.

### 4b. Jam gap (31 vs 0) — separate flow/creep issue
Even impatience-off vanilla has 0 jam, so the 31 jam teleports are not impatience. They are major-link
vehicles behind a standing downstream jam. Hypotheses to confirm: (i) a cascade — frozen minor-link
yielders (4a) back up their approaches, and once 4a clears the yielders the jams may drain; (ii) SUMO's
front-of-lane vehicles CREEP in stop-and-go (resetting `getWaitingTime`) where SumoSharp freezes solid
(observed: teleported SumoSharp vehicles sit at the exact same pos for 120 s, 0 creep); (iii) 2→1
lane-drop merge throughput (39% of the repro's nodes). Measure 4a's effect on the jam count first before
attributing (ii)/(iii).

## 5. Gates
Every existing golden byte-identical (47-teleport-jam + junctions 08/11/26/27/34/38/39/40 + determinism
D1/D8); `dotnet test` green; no tolerance loosened. On synthetic-junction: jam→vanilla-ish (0–3), yield
matching, audit PASS, mean rel-speed converging. New golden mirroring scenario 47 (unsignalized
minor-link junction + busy foe stream) asserting category+count vs vanilla.
