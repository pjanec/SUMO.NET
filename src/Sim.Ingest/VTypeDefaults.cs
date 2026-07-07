namespace Sim.Ingest;

// Fully-resolved vType parameters used by the car-following model. .rou.xml only captures
// attributes explicitly present (see VType above); everything else must be filled from SUMO's
// vClass-default tables so the engine's init matches what SUMO actually resolves internally.
//
// Values and citations below are cross-checked against BOTH the vendored source AND a
// libsumo/TraCI dump of the resolved passenger defaults (they agree -- see
// scenarios/01-single-free-flow/VTYPE_CROSSCHECK.md / golden.vtype.json). CLAUDE.md rule 6:
// diff resolved defaults against golden.state.xml/golden.vtype.json before chasing trajectory
// drift -- this resolver plus its init cross-check test exists precisely for that.
public sealed record ResolvedVType(
    string Id,
    string VClass,
    string CarFollowModel,
    double Length,
    double MinGap,
    double MaxSpeed,
    double Accel,
    double Decel,
    double EmergencyDecel,
    double ApparentDecel,
    double Sigma,
    double Tau,
    double SpeedFactor,
    double Width,
    double Height);

public static class VTypeDefaults
{
    // Only the "passenger" vClass is resolved so far -- rungs 1-4 are the only vClass in scope.
    // Generalized (rung 4) so that any attribute the rou.xml <vType> sets EXPLICITLY overrides
    // the vClass-default table below, via `override ?? default` per attribute -- e.g. rung 4's
    // leader sets maxSpeed="5.00" and both its vTypes set sigma="0". ApparentDecel is not itself
    // overridable from rou.xml in our scope; it derives from the (possibly overridden) decel,
    // matching MSCFModel.cpp:61's getCFParam(SUMO_ATTR_APPARENTDECEL, myDecel) default-to-decel
    // fallback.
    public static ResolvedVType ResolvePassenger(VType vType)
    {
        if (vType.VClass != "passenger")
        {
            throw new NotSupportedException(
                $"VTypeDefaults.ResolvePassenger only resolves vClass='passenger' (vType '{vType.Id}' has vClass='{vType.VClass}').");
        }

        // SUMOVTypeParameter.cpp getDefaultDecel default branch: return 4.5; overridable via
        // rou.xml's decel="..." attribute.
        var decel = vType.Decel ?? 4.5;

        return new ResolvedVType(
            Id: vType.Id,
            VClass: vType.VClass,
            // SUMOVTypeParameter.cpp:331 cfModel(SUMO_TAG_CF_KRAUSS) -- default CF model.
            CarFollowModel: "Krauss",
            // SUMOVehicleClass.cpp getDefaultVehicleLength default branch: return 5;
            // overridable via rou.xml's length="...".
            Length: vType.Length ?? 5.0,
            // SUMOVTypeParameter.cpp:61 minGap(2.5); overridable via rou.xml's minGap="...".
            MinGap: vType.MinGap ?? 2.5,
            // SUMOVTypeParameter.cpp:63 maxSpeed(200. / 3.6); overridable via rou.xml's
            // maxSpeed="..." (rung 4's leader sets maxSpeed="5.00" so the fast follower catches
            // up and settles into the Krauss steady-state gap).
            MaxSpeed: vType.MaxSpeed ?? (200.0 / 3.6),
            // SUMOVTypeParameter.cpp getDefaultAccel default branch: return 2.6; overridable via
            // rou.xml's accel="...".
            Accel: vType.Accel ?? 2.6,
            Decel: decel,
            // getDefaultEmergencyDecel default option -> MAX2(decel, vcDecel=9.0); overridable
            // via rou.xml's emergencyDecel="..." (default computed from the possibly-overridden
            // decel, matching SUMOVTypeParameter.cpp's MAX2(decel, 9.0) fallback).
            EmergencyDecel: vType.EmergencyDecel ?? Math.Max(decel, 9.0),
            // MSCFModel.cpp:61 getCFParam(SUMO_ATTR_APPARENTDECEL, myDecel) -- defaults to
            // (possibly-overridden) decel; not independently overridable in our scope.
            ApparentDecel: decel,
            // SUMOVTypeParameter.cpp getDefaultImperfection default branch: return 0.5;
            // overridable via rou.xml's sigma="..." (rungs 1/4 set sigma="0" for determinism).
            Sigma: vType.Sigma ?? 0.5,
            // MSCFModel.cpp:63 getCFParam(SUMO_ATTR_TAU, 1.0); overridable via rou.xml's
            // tau="...".
            Tau: vType.Tau ?? 1.0,
            // SUMOVTypeParameter.cpp:317 speedFactor("normc", 1.0, 0.0, 0.2, 2.0) -- mean 1.0.
            // Phase 1 has no System.Random / RNG at all (CLAUDE.md), and rung 1/4's config.sumocfg
            // additionally forces default.speeddev="0", so the drawn speedFactor is exactly its
            // mean, 1.0, with no per-vehicle deviation to model yet; overridable via rou.xml's
            // speedFactor="..." for a fixed (non-distributional) override.
            SpeedFactor: vType.SpeedFactor ?? 1.0,
            // SUMOVTypeParameter.cpp:65 width(1.8).
            Width: 1.8,
            // SUMOVTypeParameter.cpp:66 height(1.5).
            Height: 1.5);
    }
}
