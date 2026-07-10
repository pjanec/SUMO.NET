using Sim.Ingest;

namespace Sim.Core;

// Rung R6: MSCFModel_Rail -- the rail traction/resistance car-following model
// (sumo/src/microsim/cfmodels/MSCFModel_Rail.{h,cpp}). Unlike Krauss (a constant accel/decel
// bound), a train's acceleration is speed-dependent: a = (traction(v) - resistance(v) - grade) /
// rotWeight, where traction and resistance come from the train's parametric curves. This gives the
// characteristic rail accel profile (strong pull at low speed, tapering as speed rises and
// resistance grows). Phase-1 scope: the PARAMETRIC curves (maxPower/maxTraction + resCoef_*), on a
// FLAT track (grade 0, so the gravity term vanishes); the built-in per-type lookup tables and slope
// handling are deferred. Euler integration only (CLAUDE.md phase-1). Only reached for a vType whose
// resolved CarFollowModel == "Rail"; every other model is untouched.
internal static class RailModel
{
    // MSCFModel_Rail::TrainParams::getTraction (MSCFModel_Rail.cpp:47) parametric arm:
    //   maxPower != INVALID -> MIN2(maxPower / speed, maxTraction). At speed 0, maxPower/0 = +inf in
    //   IEEE 754, so Min(+inf, maxTraction) = maxTraction, matching SUMO's MIN2 exactly.
    private static double Traction(double speed, ResolvedVType v) =>
        Math.Min(v.MaxPower / speed, v.MaxTraction); // kN

    // MSCFModel_Rail::TrainParams::getResistance (MSCFModel_Rail.cpp:37) parametric arm:
    //   resCoef_quadratic*v^2 + resCoef_linear*v + resCoef_constant.
    private static double Resistance(double speed, ResolvedVType v) =>
        v.ResCoefQuadratic * speed * speed + v.ResCoefLinear * speed + v.ResCoefConstant; // kN

    private static double RotWeight(ResolvedVType v) => v.Weight * v.MassFactor; // tons

    // MSCFModel_Rail::maxNextSpeed (MSCFModel_Rail.cpp:216). Flat track => grade term gr = 0, so
    // totalRes == resistance. a = (traction - totalRes) / rotWeight (kN/t == N/kg == m/s^2);
    // maxNextSpeed = speed + ACCEL2SPEED(a). Capped at vmax (== ResolvedVType.MaxSpeed, which the
    // Rail resolver already set from the trainType).
    public static double MaxNextSpeed(double speed, ResolvedVType v, double dt)
    {
        if (speed >= v.MaxSpeed)
        {
            return v.MaxSpeed;
        }

        var totalRes = Resistance(speed, v); // grade 0 -> no gravity term
        var trac = Traction(speed, v);
        var a = (trac - totalRes) / RotWeight(v);
        return speed + KraussModel.Accel2Speed(a, dt);
    }

    // MSCFModel_Rail::minNextSpeed (MSCFModel_Rail.cpp, Euler branch): a = decl + totalRes/rotWeight;
    // vMin = MAX2(speed - ACCEL2SPEED(a), 0). minNextSpeedEmergency returns the same value.
    public static double MinNextSpeed(double speed, ResolvedVType v, double dt)
    {
        var totalRes = Resistance(speed, v); // grade 0
        var a = v.Decel + totalRes / RotWeight(v);
        return Math.Max(speed - KraussModel.Accel2Speed(a, dt), 0.0);
    }

    // DEFERRED (rung R6 minimal): MSCFModel_Rail::followSpeed (MSCFModel_Rail.cpp:181), the
    // moving-block leader safety speed. Not exercised by the committed single free-running train
    // anchor (no leader), so it is not ported here -- a two-train Rail-model scenario would drive it.
    // The base MSCFModel::finalizeSpeed (the Rail model does not override it), identical to
    // IdmModel.FinalizeSpeed EXCEPT that vMin/vMax are bounded by the Rail model's own
    // min/maxNextSpeed. No dawdle (MSCFModel_Rail does not override patchSpeedBeforeLC).
    public static double FinalizeSpeed(
        double oldV,
        double vPos,
        double vStop,
        double laneVehicleMaxSpeed,
        ResolvedVType vType,
        double dt,
        double actionStepLengthSecs)
    {
        // minNextSpeedEmergency == minNextSpeed for the Rail model (MSCFModel_Rail.cpp).
        var vMinEmergency = MinNextSpeed(oldV, vType, dt);
        var vMin = Math.Min(MinNextSpeed(oldV, vType, dt), Math.Max(vPos, vMinEmergency));

        const double factor = 1.0; // getFriction()==1 in phase 1

        var aMax = ((Math.Max(laneVehicleMaxSpeed, vPos) * factor) - oldV) / actionStepLengthSecs;
        var vMax = Math.Min(oldV + KraussModel.Accel2Speed(aMax, dt), Math.Min(MaxNextSpeed(oldV, vType, dt), vStop));
        vMax = Math.Max(vMin, vMax);

        return vMax;
    }
}
