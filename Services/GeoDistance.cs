using NetTopologySuite.Geometries;

namespace ChessApp.Backend.Services;

/// <summary>
/// Great-circle distance between two WGS84 points (X = longitude, Y = latitude).
/// NetTopologySuite's Point.Distance is planar, so on lat/long coordinates it returns
/// degrees rather than metres; use this for any distance computed in memory.
/// </summary>
public static class GeoDistance
{
    private const double EarthRadiusKm = 6371.0088;

    public static double Kilometers(Point a, Point b)
    {
        var lat1 = ToRadians(a.Y);
        var lat2 = ToRadians(b.Y);
        var dLat = lat2 - lat1;
        var dLon = ToRadians(b.X - a.X);

        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
