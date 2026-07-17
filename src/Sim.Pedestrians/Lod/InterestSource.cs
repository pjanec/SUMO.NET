using Sim.Core.Orca;

namespace Sim.Pedestrians.Lod;

// A movable sim-LOD interest source (docs/PEDESTRIAN-DESIGN.md §5; docs/PEDESTRIAN-POC-PLAN.md POC-3):
// any low-power ped within PromoteRadius of ANY active source promotes to high-power; a promoted ped
// only demotes once it is outside EVERY active source's (larger) DemoteRadius for a dwell period
// (PedLodManager owns the dwell timing). DemoteRadius > PromoteRadius is spatial hysteresis -- without
// it, a ped sitting exactly at one shared radius could flip every step.
//
// Position is a mutable property (not a constructor-only value) because sources are movable: an
// avatar, an external car, or an IG camera frustum carries its bubble with it, updated once per step
// by whoever owns that source (PedLodManager.Step reads it as FROZEN start-of-step state -- it never
// mutates a source itself).
public sealed class InterestSource
{
    public Vec2 Position { get; set; }
    public double PromoteRadius { get; }
    public double DemoteRadius { get; }

    public InterestSource(Vec2 position, double promoteRadius, double demoteRadius)
    {
        if (promoteRadius <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(promoteRadius), "PromoteRadius must be positive.");
        }

        if (demoteRadius <= promoteRadius)
        {
            throw new ArgumentException(
                "DemoteRadius must be strictly greater than PromoteRadius (spatial hysteresis).",
                nameof(demoteRadius));
        }

        Position = position;
        PromoteRadius = promoteRadius;
        DemoteRadius = demoteRadius;
    }
}
