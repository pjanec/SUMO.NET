namespace Sim.Core;

// The engine seam (DESIGN.md "build order"): implemented starting Task 2. Not implemented here.
public interface IEngine
{
    void LoadScenario(string netXmlPath, string rouXmlPath, string sumocfgPath);

    TrajectorySet Run(int steps);

    // B1 external-obstacle input surface (DESIGN.md "Two futures"): a live object (non-SUMO vehicle,
    // pedestrian, detection) injected between the offline model and the reducer. Inert-when-absent.
    void AddObstacle(string id, string laneId, double frontPos, double length,
                     double startTime = double.NegativeInfinity, double endTime = double.PositiveInfinity);

    // B5-i: the MOVING generalization of AddObstacle -- same add-or-replace-by-id contract, but
    // the obstacle also carries a velocity the engine dead-reckons each step (Engine.AdvanceObstacles,
    // Input phase, before PlanMovements) until the owner calls UpdateObstacle with a fresh reading.
    // speed=0 here degenerates to a static obstacle, but callers that know they are static should
    // keep using AddObstacle (unchanged, byte-identical to B1).
    void AddMovingObstacle(string id, string laneId, double frontPos, double length,
                           double speed, double maxDecel,
                           double startTime = double.NegativeInfinity, double endTime = double.PositiveInfinity);

    // B5-i: per-step correction from the external layer that owns this obstacle's real motion
    // (navmesh/RVO agent, live detection) -- replaces the dead-reckoned FrontPos/Speed with a
    // fresh reading, preserving every other field (Length/LaneId/StartTime/EndTime/MaxDecel) via
    // `record with`. No-op if `id` is not currently registered (inert-when-absent).
    void UpdateObstacle(string id, double frontPos, double speed);

    void RemoveObstacle(string id);
    void ClearObstacles();
}
