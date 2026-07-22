# Live-city in the interactive viewers — tracker

Checklist for `docs/LIVE-CITY-VIEWERS-TASKS.md` (design: `docs/LIVE-CITY-VIEWERS-DESIGN.md`). A box is
ticked **only** after Opus verifies the task's success conditions first-hand (diff read + gate/smoke
re-run; desktop-only aesthetic sign-off is the user's and noted as such — never faked from a headless run).

**Standing gate on every `src/`-touching tick:** `dotnet test Traffic.sln -c Release` at the committed
baseline + `Sim.Bench` determinism hash unchanged (single + parallel). Capture the baseline fresh on the
first `src/` task of each stage and repeat the numbers here as each box is ticked.

Baseline (captured 2026-07-22, clean checkout at `d1b1638`): **895 pass / 4 skip** —
ParityTests 654/4, Pedestrians 227, IgBridge 11, DotRecast 2, Host 1; determinism hash
**`D96213B7BB4021A7`** (single == parallel). This is the standing bar for every `src/`-touching tick.
Re-capture fresh per task (other sessions may edit the engine).

## Stage A — SumoSharp.LiveCity shared host
- [ ] **A1** project scaffold (`src/Sim.LiveCity`, packs `SumoSharp.LiveCity.0.1.0`)
- [ ] **A2** `LiveCitySim`/`LiveCityConfig`/`LiveCitySnapshot` — coupled recipe, per-tick order, read-back; test asserts cars>0/peds>0/CarYieldObservations>0 + deterministic double-run + YIELD A/B

## Stage B — Raylib 2D live-city (real-time)
- [ ] **B1** `DemoKind.LiveCity` + `LiveCityOverlay` — cars + peds drawn in one frame; smoke cars>0 && peds>0
- [ ] **B2** click-select vehicle + SUMO-id label; hit-test unit test

## Stage C — Shared record/replay + playback (Raylib)
- [ ] **C1** `.simrec` format + `ReplicationRecorder` (cars + peds); round-trip test
- [ ] **C2** `ReplicationFileSource` + `PedReplicationFileSource` (seekable); replay==live within tol; SeekTo test
- [ ] **C3** Raylib playback panel (play/pause/restart/speed/frame-step + drag slider) + `--record`/`--replay`

## Stage D — City3D local (live + replay, click, Z)
- [ ] **D1** drop cars-XOR-peds; `--live-city` renders cars + peds over demo_city/box (legacy modes intact)
- [ ] **D2** honor Z on local road/car meshes; synthetic elevated-net test (non-zero Z→non-zero Y; flat→0)
- [ ] **D3** Godot playback controls + `--live-city --replay <file>`
- [ ] **D4** click ray-pick vehicle → highlight + id; scripted-pick test

## Stage E — City3D remote (combined DDS)
- [ ] **E1** Z on the replication wire (GeometryCodec + DDS geometry); round-trip on elevated net; hot path untouched
- [ ] **E2** vehicle name once-per-spawn on the wire; per-frame record unchanged; remote id resolves
- [ ] **E3** combined cars+peds DDS producer (`Sim.Host.App --live-city`), one net; inmem self-consume both
- [ ] **E4** dual subscriber (`--transport=dds --live-city`), two-process round-trip renders cars+peds + Z + ids

Status: **DRAFT — awaiting owner sign-off on the design before implementation begins.**

Deferred (owner-confirmed, separate branch): cooperative lane-change overlap fix
(`docs/LANE-CHANGE-OVERLAP-SPEC.md`) and crossing tunneling.
