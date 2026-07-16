#!/usr/bin/env python3
"""
auto_parking.py — turn a cut sub-area route file into a NO-POPPING one by mapping
every internal origin/destination to a parkingArea sink, fully automatically.

This is the parking-sink half of the no-cheating rule (the ~27% of cut demand whose
real O/D is inside the box). It is a *preprocessing* step (authoring), like cutRoutes:
input = cropped net + cut routes; output = an additional-file of parkingAreas plus a
rewritten route file. Measures how automatable the parking layer is (answer: fully).

Rules:
  * internal DESTINATION -> append <stop parkingArea=... > that outlasts the sim:
    the car pulls off the running lane into the lot and stays = a believable sink,
    never a mid-road vanish.
  * internal ORIGIN -> departPos="stop" + a short leading parkingArea stop: the car
    is inserted already parked (off-road) and pulls out = a believable source.
  * one parkingArea per internal-endpoint edge (lane _0), roadsideCapacity sized to
    demand on that edge (a <rerouter> overflow variant is a later refinement).

Usage: auto_parking.py <sub.net.xml> <sub.rou.xml> <out.add.xml> <out.rou.xml>
"""
import sys, collections, xml.etree.ElementTree as ET
import sumolib

net_f, rou_f, add_out, rou_out = sys.argv[1:5]
net = sumolib.net.readNet(net_f)
fringe = {e.getID() for e in net.getEdges()
          if e.getFunction() != 'internal' and e.is_fringe()}

PARK_FOREVER = 100000   # >> sim end: destination cars park and stay (the sink)
PULLOUT      = 5        # origin cars sit briefly then merge into traffic

tree = ET.parse(rou_f)
root = tree.getroot()
vehicles = root.findall('vehicle')

# 1. which internal edges need a parkingArea, and how much capacity each needs
demand = collections.Counter()
plan = []   # (veh_el, first_edge_or_None, last_edge_or_None)
for v in vehicles:
    edges = v.find('route').get('edges').split()
    first, last = edges[0], edges[-1]
    o = first if first not in fringe else None
    d = last  if last  not in fringe else None
    if o: demand[o] += 1
    if d: demand[d] += 1
    plan.append((v, o, d))

# 2. emit one parkingArea per internal-endpoint edge, on lane _0
add = ET.Element('additional')
for edge_id, cap in sorted(demand.items()):
    lane = net.getEdge(edge_id).getLane(0)
    L = lane.getLength()
    ET.SubElement(add, 'parkingArea', {
        'id': f'pa_{edge_id}',
        'lane': f'{edge_id}_0',
        'startPos': '2.00',
        'endPos': f'{max(4.0, L - 2.0):.2f}',
        'roadsideCapacity': str(max(1, cap)),
    })
ET.ElementTree(add).write(add_out, encoding='UTF-8', xml_declaration=True)

# 3. rewrite routes: prepend origin stop / append destination stop
for v, o, d in plan:
    if 'arrival' in v.attrib:      # cutRoutes metadata, not an input attr
        del v.attrib['arrival']
    route = v.find('route')
    ridx = list(v).index(route)
    if o:
        v.set('departPos', 'stop')          # insert already parked (off-road)
        stop = ET.Element('stop', {'parkingArea': f'pa_{o}', 'duration': str(PULLOUT)})
        v.insert(ridx + 1, stop)            # first stop = origin
    if d:
        ET.SubElement(v, 'stop', {'parkingArea': f'pa_{d}', 'duration': str(PARK_FOREVER)})
tree.write(rou_out, encoding='UTF-8', xml_declaration=True)

npark = sum(1 for _, o, d in plan if o or d)
print(f"parkingAreas: {len(demand)}  |  vehicles touched: {npark}/{len(vehicles)}"
      f"  (origin={sum(1 for _,o,_ in plan if o)}, dest={sum(1 for _,_,d in plan if d)})")
