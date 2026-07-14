using CycloneDDS.Schema;

namespace Sim.Replication.Dds;

// Reverse-channel viewer command topic (docs/SUMOSHARP-NATIVE-VIEWER.md remote control). A view-only remote
// process has no engine of its own, so to drive the publisher's simulation (pause/resume, playback speed,
// restart, clear obstacles, random traffic, inject an obstacle) it publishes these commands and the publisher
// subscribes + applies them to its EngineHost. Keyed by WriterId so multiple remotes are distinct instances;
// the publisher applies a given key's command only when its Seq advances (so a durable re-delivery to a
// late-joining publisher is idempotent). Kind values mirror Sim.Viewer.Core.ViewerCommandKind.
[DdsTopic]
public partial struct DdsViewerCommand
{
    [DdsKey, DdsId(0)] public int WriterId;
    [DdsId(1)] public uint Seq;
    [DdsId(2)] public byte Kind;   // ViewerCommandKind
    [DdsId(3)] public double Value; // scalar param (speed, ...)
    [DdsId(4)] public double X;     // obstacle world X (InjectObstacle)
    [DdsId(5)] public double Y;     // obstacle world Y (InjectObstacle)
    [DdsId(6)] public byte Flag;    // bool param (pause on/off, random on/off)
}
