namespace Sim.Core;

// SUMOSHARP-API.md §7 (interpolation hook): one vehicle's render state interpolated between the two
// most-recently-published snapshots, so a host rendering faster than the sim ticks (e.g. 120 fps over a
// 10 Hz sim) draws smooth motion instead of stepping. Render-facing floats only -- this is "where to draw
// it," not "what the sim computed" (the parity-exact doubles live on the snapshot / VehicleState). A
// value type: no allocation on the host render path.
public readonly struct InterpolatedVehicle
{
    public InterpolatedVehicle(
        VehicleHandle handle, float posX, float posY, float posZ, float angle, float speed)
    {
        Handle = handle;
        PosX = posX;
        PosY = posY;
        PosZ = posZ;
        Angle = angle;
        Speed = speed;
    }

    public VehicleHandle Handle { get; }
    public float PosX { get; }
    public float PosY { get; }
    public float PosZ { get; }
    public float Angle { get; }   // degrees, interpolated along the shortest arc
    public float Speed { get; }
}
