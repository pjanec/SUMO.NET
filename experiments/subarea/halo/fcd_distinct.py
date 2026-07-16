#!/usr/bin/env python3
"""Extract the set of distinct vehicle IDs that actually appear (are physically
present, at any timestep) on an inner-box edge, from filtered FCD output.
This is ground truth for 'touched the inner box during the simulation' --
unlike vehroute-output, FCD only records what actually happened."""
import sys
import xml.etree.ElementTree as ET

def distinct_vehicles(fcd_path):
    vids = set()
    n_timesteps = 0
    for event, elem in ET.iterparse(fcd_path, events=("end",)):
        if elem.tag == "timestep":
            n_timesteps += 1
            for v in elem.findall("vehicle"):
                vids.add(v.get("id"))
            elem.clear()
    return vids, n_timesteps

if __name__ == "__main__":
    path = sys.argv[1]
    vids, nt = distinct_vehicles(path)
    print(f"{path}: {len(vids)} distinct vehicles across {nt} timesteps")
