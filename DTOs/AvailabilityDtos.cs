namespace ChessApp.Backend.DTOs;

public class SetAvailabilityRequest
{
    public bool IsAvailable { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool HasBoard { get; set; }
    public int ExpiresInHours { get; set; } = 4;
}

public class SetAvailabilityResponse
{
    public bool IsAvailable { get; set; }
    public DateTime ExpiresAt { get; set; }
    public LocationDto Location { get; set; } = null!;
}

public class LocationDto
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Address { get; set; }
}

public class NearbyUserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string? FullName { get; set; }
    public string? PhotoUrl { get; set; }
    public string? Bio { get; set; }

    public int? FideRating { get; set; }
    public int? ChessComRating { get; set; }
    public int? LichessRating { get; set; }

    public bool HasBoard { get; set; }
    public decimal DistanceKm { get; set; }
}

public class NearbyUsersResponse
{
    public List<NearbyUserDto> Users { get; set; } = new();
    public int Count => Users.Count;
}
