# evac-grid — panic-evacuation spine network (PARITY-EXEMPT)

A hand-built 4×4 priority-junction grid (`netgenerate --grid --grid.number=4 --grid.length=80
--grid.attach-length=60 --no-turnarounds --tls.guess=false`), with outward boundary stubs used as
flee exits. Nodes sit at x,y ∈ {60, 140, 220, 300}; the grid centre is ≈ (180, 180).

This scenario is **parity-EXEMPT**: it exercises the external evacuation layer (`Sim.Evac`,
`docs/PANIC-EVAC.md`), **not** the SUMO-parity core. There is deliberately **no golden** (no
`golden.fcd.xml` / `tolerance.json` / `provenance.txt`) and **no `rou.xml` demand** — the driving
core stays byte-identical whether or not this net exists (determinism hash unchanged), and the
`EvacSpineTests` spawn their vehicles at runtime via `Engine.SpawnVehicle`, so the offline test loop
needs neither SUMO nor a committed route file.

Only `net.net.xml` is committed. It was authored once with `netgenerate` (SUMO 1.18, network-side)
purely for geometry; the exact SUMO version is irrelevant here because nothing compares against a
SUMO trajectory.
