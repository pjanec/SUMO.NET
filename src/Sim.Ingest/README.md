# SumoSharp.Ingest

Parsers and data model for Eclipse SUMO input files — network (`.net.xml`), demand (`.rou.xml`), and
config (`.sumocfg`) — plus the lane/edge/junction network model and lane geometry used by the
[SumoSharp.Core](https://www.nuget.org/packages/SumoSharp.Core) microsimulation engine.

Most users install **SumoSharp.Core**, which depends on this package; install this one directly only
if you want the SUMO file parsers and network model without the engine.

> **Unofficial, independent reimplementation.** Not affiliated with or endorsed by the Eclipse SUMO
> project. "SUMO" is a trademark of the Eclipse Foundation.

## License

`EPL-2.0 OR GPL-2.0-or-later` (derivative of SUMO). See
[the repository](https://github.com/pjanec/SumoSharp) for details.
