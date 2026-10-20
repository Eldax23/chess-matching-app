using ChessApp.Backend.Services;
using NetTopologySuite.Geometries;
using Xunit;

namespace ChessApp.Backend.Tests;

public class GeoDistanceTests
{
    private static readonly GeometryFactory Geometry = new(new PrecisionModel(), 4326);

    private static Point At(double latitude, double longitude) =>
        Geometry.CreatePoint(new Coordinate(longitude, latitude));

    [Fact]
    public void ReturnsKilometres_NotDegrees()
    {
        // Downtown Cairo to Giza, about 12 km
        var km = GeoDistance.Kilometers(At(30.0444, 31.2357), At(29.9792, 31.1342));

        Assert.InRange(km, 12.0, 12.4);
    }

    [Fact]
    public void SamePoint_IsZero()
    {
        Assert.Equal(0, GeoDistance.Kilometers(At(30, 31), At(30, 31)), 6);
    }
}
