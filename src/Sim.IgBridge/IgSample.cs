namespace Sim.IgBridge;

// The IG-native record schema (docs/IGBRIDGE-DECISIONS.md §3). The proprietary IG accepts a lifecycle
// triple -- entity-created / entity-updated / entity-removed -- carrying, per the owner's Q1/Q3
// answers, a PLANAR pose only: (x, y, headingDeg) in SUMO metres + navi-degrees. No z / pitch / roll:
// the IG owns its terrain and does its own conformal ground-clamping. `t` is SIM time (seconds) and is
// the sole timing authority; a sample's pose is the pose AT `t`, so consecutive `Upd` for an entity
// satisfy dPos ~= speed*dt and the IG's 2-sample DR never erratically jumps.
public enum IgRecordKind : byte
{
    New = 0, // entity-created (id, model, initial pose)
    Upd = 1, // entity-updated (the per-sample pose)
    Del = 2, // entity-removed (id)
}

// Selects the IG 3D model on `New`. Extend as more entity classes are fed.
public enum IgEntityModel : byte
{
    Car = 0,
    Ped = 1,
}

// One IG-native record. A value type (no allocation per emit); the same shape is flushed to the JSONL
// trace (T1.2) and pushed onto the in-memory ring. `Model` is meaningful only on `New`; `X/Y/H` are
// meaningful on `New`/`Upd` and ignored on `Del`.
public readonly struct IgSample
{
    public IgSample(IgRecordKind kind, string id, double t, IgEntityModel model, double x, double y, float headingDeg)
    {
        Kind = kind;
        Id = id;
        T = t;
        Model = model;
        X = x;
        Y = y;
        HeadingDeg = headingDeg;
    }

    public IgRecordKind Kind { get; }
    public string Id { get; }
    public double T { get; }
    public IgEntityModel Model { get; }
    public double X { get; }
    public double Y { get; }
    public float HeadingDeg { get; }

    public static IgSample Created(string id, double t, IgEntityModel model, double x, double y, float headingDeg)
        => new(IgRecordKind.New, id, t, model, x, y, headingDeg);

    public static IgSample Updated(string id, double t, double x, double y, float headingDeg)
        => new(IgRecordKind.Upd, id, t, default, x, y, headingDeg);

    public static IgSample Removed(string id, double t)
        => new(IgRecordKind.Del, id, t, default, 0.0, 0.0, 0f);
}
