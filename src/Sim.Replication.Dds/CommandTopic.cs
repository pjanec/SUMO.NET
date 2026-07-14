using CycloneDDS.Schema;

namespace Sim.Replication.Dds;

// Reverse-channel viewer command topic (docs/SUMOSHARP-NATIVE-VIEWER.md remote control). A view-only remote
// process has no engine of its own, so to drive the publisher's simulation (pause/resume, playback speed,
// restart, clear obstacles, random traffic, inject an obstacle) it publishes these commands and the publisher
// subscribes + applies them to its EngineHost. Keyed by WriterId so multiple remotes are distinct instances;
// the publisher applies a given key's command only when its Seq advances (so a durable re-delivery to a
// late-joining publisher is idempotent). Kind values mirror Sim.Viewer.Core.ViewerCommandKind.
//
// QoS is declared on the TYPE via [DdsQos] (CycloneDDS.NET code-first DSL) rather than a hand-built QoS
// handle: RELIABLE (no command dropped), TRANSIENT_LOCAL (commands issued during the ~1-2s writer<->reader
// discovery window are retained and delivered on match -- volatile would lose them), KEEP_LAST(256) (a
// burst / the whole pre-match backlog is retained, not clobbered to the newest). The runtime applies this
// automatically to any writer/reader created for the topic (2-arg ctor, no explicit qos handle).
[DdsTopic]
[DdsQos(
    Reliability = DdsReliability.Reliable,
    Durability = DdsDurability.TransientLocal,
    HistoryKind = DdsHistoryKind.KeepLast,
    HistoryDepth = 256)]
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
