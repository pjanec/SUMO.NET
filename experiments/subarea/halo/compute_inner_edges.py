#!/usr/bin/env python3
"""Compute the set of edge IDs whose center lies within the INNER bbox.
Edge IDs are stable across crops (netconvert --keep-edges.in-boundary keeps
edge IDs unchanged), so this set, computed once on the full macro net, is
reused unmodified for every halo depth and for the h=INF (full macro) run.
"""
import sys
import sumolib

INNER = (3450.0, 3450.0, 5250.0, 5250.0)  # xmin, ymin, xmax, ymax

def edge_center(edge):
    shape = edge.getShape()
    xs = [p[0] for p in shape]
    ys = [p[1] for p in shape]
    return (sum(xs) / len(xs), sum(ys) / len(ys))

def main():
    net = sumolib.net.readNet('../synth_macro.net.xml')
    xmin, ymin, xmax, ymax = INNER
    inner_edges = []
    for edge in net.getEdges():
        if edge.isSpecial():
            continue
        cx, cy = edge_center(edge)
        if xmin <= cx <= xmax and ymin <= cy <= ymax:
            inner_edges.append(edge.getID())
    inner_edges.sort()
    with open('inner_edges.txt', 'w') as f:
        for eid in inner_edges:
            f.write(eid + '\n')
    print(f'total edges: {len(net.getEdges())}')
    print(f'inner-box edges: {len(inner_edges)}')

if __name__ == '__main__':
    main()
