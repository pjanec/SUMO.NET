# rail-crossing-demo — level crossing with queuing road traffic (rung R5)

A **Sim.Viz visual demo** of the SUMO rail level crossing (`MSRailCrossing`, engine rung **R5**).
Not a parity scenario — the R5 golden anchor is `scenarios/51-rail-crossing`. This one runs the
same engine feature at a livelier scale so the replay reads clearly on a phone.

**Replay artifact:** https://claude.ai/code/artifact/4b48a235-c4b7-44f8-ba9c-e196863526ab

## The scene

A single rail track `WX → XE` crosses a two-way road at node **X**, whose type is `rail_crossing`.
While a train body occupies X, the crossing closes its controlled **road** links (the G/y/r/u state
machine), so road cars brake and yield short of the tracks, then release once the crossing re-opens.

- **Rail:** two trains (`vClass="rail"`, Krauss) run the track in convoy — `t0` (depart 0) and `t1`
  (depart 60). The crossing sits ~250 m from the trains' start so each train is still accelerating
  (long body-occupancy → long closure → a real queue) when it crosses.
- **Road:** 21 passenger cars (`sigma=0`, `maxSpeed=13.89`) run the road in both directions
  (northbound `SX/XN`, southbound `NX/XS`), released in two waves timed to the two train crossings.

## Verified from the engine FCD (`Sim.Run` → `engine.fcd.xml`, 148 steps)

| Train | Occupies the crossing | Road queue it produces |
|---|---|---|
| `t0` | t ≈ 38–50 | builds to **7 cars** stopped at the tracks (t=45→60), releases by t≈63 |
| `t1` | t ≈ 98–110 | builds to **5 cars** stopped at the tracks (t≈106→118), releases by t≈124 |

Road cars halt with their front ~at the crossing (world y ≈ the rail line) only while a train
occupies X, and every car eventually clears (no deadlock). The trains render as long amber boxes
(rail vClass palette entry added to Sim.Viz); the passenger cars are blue.

## Reproduce

```
netconvert --node-files nodes.nod.xml --edge-files edges.edg.xml --output-file net.net.xml --no-turnarounds true
dotnet run --project src/Sim.Run -c Release -- scenarios/_bench/rail-crossing-demo --fcd-out scenarios/_bench/rail-crossing-demo/engine.fcd.xml
dotnet run --project src/Sim.Viz -c Release -- scenarios/_bench/rail-crossing-demo --fcd scenarios/_bench/rail-crossing-demo/engine.fcd.xml
```

`engine.fcd.xml` and `replay.html` are regenerated (git-ignored); only the inputs are committed.
