# emergency-corridor-demo — ambulance rescue lane (rungs ER3 + ER4)

A **Sim.Viz visual demo** of emergency-vehicle give-way: a blue-light ambulance comes up behind a
column of slow cars, which peel aside one after another to clear a rescue lane. Exercises engine
rungs **ER3** (per-ego bluelight detection) and **ER4** (multi-lane give-way execution). Not a
parity scenario — the give-way rungs are behavioral (no SUMO golden, since SUMO forms its rescue
lane by sublane lateral alignment while the engine adapts it to a lane change); their
behavioral-property anchors are `scenarios/53-giveway-single .. 55-giveway-drift`.

**Replay artifact:** https://claude.ai/code/artifact/06fe2905-9e89-47f9-a4b1-2d1a3cbf55c5

## The scene

One two-lane one-way edge `e0` (`e0_0` right, `e0_1` left), laid out vertically so the
one-lane-width give-way is legible under Sim.Viz's fit-to-view camera.

- **Column:** five slow cars (`vClass="passenger"`, `sigma=0`, `maxSpeed=6`) in a tight ~35 m
  column in the right lane `e0_0`.
- **Ambulance:** one EV (`vClass="emergency"`, `hasBluelight="true"`, `maxSpeed=13.89`) behind the
  column in the same lane. As the EV closes within siren range in a car's lane, that car vacates to
  `e0_1`; the EV holds `e0_0` (a bluelight EV makes no overtaking lane change of its own — it relies
  on others clearing) and speeds through the opened lane.

## Verified from the engine FCD (`Sim.Run` → `engine.fcd.xml`, 75 steps)

Each car vacates `e0_0 → e0_1`, then merges back `e0_1 → e0_0` nine seconds later — a progressive
rescue lane that tracks the advancing ambulance:

| Car | Vacates right→left | Merges back |
|---|---|---|
| `car0` | t=7  | t=16 |
| `car1` | t=12 | t=21 |
| `car2` | t=17 | t=26 |
| `car3` | t=22 | t=31 |
| `car4` | t=27 | t=36 |

The ambulance never changes lane and overtakes the whole column; minimum same-lane centre-to-centre
gap over the whole run is **21.4 m** (no collision). The EV renders red (`emergency` vClass); the
cars are blue.

## Reproduce

```
netconvert --node-files nodes.nod.xml --edge-files edges.edg.xml --output-file net.net.xml --no-turnarounds true
dotnet run --project src/Sim.Run -c Release -- scenarios/_bench/emergency-corridor-demo --fcd-out scenarios/_bench/emergency-corridor-demo/engine.fcd.xml
dotnet run --project src/Sim.Viz -c Release -- scenarios/_bench/emergency-corridor-demo --fcd scenarios/_bench/emergency-corridor-demo/engine.fcd.xml
```

`engine.fcd.xml` and `replay.html` are regenerated (git-ignored); only the inputs are committed.
