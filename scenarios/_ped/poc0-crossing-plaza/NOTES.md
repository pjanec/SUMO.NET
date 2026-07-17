# POC-0 — pedestrian test network

Foundational scenario for the pedestrian POC ladder (`docs/PEDESTRIAN-POC-PLAN.md` POC-0).

## What it contains
- A 4-arm **signalized** junction `c` (traffic_light) with two-lane approaches.
- **Sidewalks** on every approach (lanes with `allow="pedestrian"`).
- Four **pedestrian crossings** (`<edge function="crossing">`), TLS-controlled.
- Eight **walkingAreas** (`<edge function="walkingarea">`) at the junction corners.
- `walkable.add.xml`: a **plaza** and a **parking-lot** surface as walkable polygons
  (not SUMO ped infrastructure), plus parking entry/exit POIs for the car mode-switch.

## Regeneration (authoring-side; SUMO required, never in the test loop)
```
netconvert --node-files=nodes.nod.xml --edge-files=edges.edg.xml \
  --sidewalks.guess --crossings.guess --tls.guess --output-file=net.net.xml
```
Generated with Eclipse SUMO 1.20.0 (matches `SUMO_VERSION`). The committed `net.net.xml`
is the hermetic test input; SUMO is not needed to run `dotnet test`.
