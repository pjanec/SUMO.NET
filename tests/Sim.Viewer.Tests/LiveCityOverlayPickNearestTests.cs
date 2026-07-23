using Sim.Core;
using Sim.LiveCity;
using Sim.Viewer;
using Xunit;

namespace Sim.Viewer.Tests;

// docs/LIVE-CITY-VIEWERS-TASKS.md Stage B2 success condition: "a headless unit test of the hit-test picks
// the nearest car to a click point (and none when the click is far)". LiveCityOverlay.PickNearest is a
// pure static helper (no Raylib/window dependency), so this exercises it directly.
public sealed class LiveCityOverlayPickNearestTests
{
    private static LiveCityCar Car(uint index, double x, double y) =>
        new(new VehicleHandle(index, 1), x, y, z: 0.0, angleDeg: 0.0, length: 5.0, width: 1.8, name: $"veh{index}");

    [Fact]
    public void PicksNearestCarWithinRadius()
    {
        var cars = new[]
        {
            Car(0, 0.0, 0.0),
            Car(1, 10.0, 0.0),
            Car(2, 10.5, 0.0), // closest to the click point below, but still further than car 1's exact match
        };

        // Click right on top of car 1 -- car 1 must win over both car 0 (far away) and car 2 (also close,
        // but strictly farther than car 1 from the click point).
        var idx = LiveCityOverlay.PickNearest(cars, wx: 10.0, wy: 0.0, maxDist: 4.0);

        Assert.Equal(1, idx);
    }

    [Fact]
    public void PicksNearestAmongMultipleCandidatesWithinRadius()
    {
        var cars = new[]
        {
            Car(0, 100.0, 100.0), // far outside maxDist -- must never be picked
            Car(1, 2.0, 0.0),     // distance 2.0 from the click point
            Car(2, 0.0, 1.0),     // distance 1.0 from the click point -- nearest
        };

        var idx = LiveCityOverlay.PickNearest(cars, wx: 0.0, wy: 0.0, maxDist: 4.0);

        Assert.Equal(2, idx);
    }

    [Fact]
    public void ReturnsMinusOneWhenClickIsBeyondMaxDist()
    {
        var cars = new[]
        {
            Car(0, 50.0, 50.0),
            Car(1, -30.0, 10.0),
        };

        var idx = LiveCityOverlay.PickNearest(cars, wx: 0.0, wy: 0.0, maxDist: 4.0);

        Assert.Equal(-1, idx);
    }

    [Fact]
    public void ReturnsMinusOneForEmptyCarList()
    {
        var idx = LiveCityOverlay.PickNearest(System.Array.Empty<LiveCityCar>(), wx: 0.0, wy: 0.0, maxDist: 4.0);

        Assert.Equal(-1, idx);
    }

    [Fact]
    public void PickIsInclusiveAtExactlyMaxDist()
    {
        var cars = new[] { Car(0, 4.0, 0.0) };

        var idx = LiveCityOverlay.PickNearest(cars, wx: 0.0, wy: 0.0, maxDist: 4.0);

        Assert.Equal(0, idx);
    }
}
