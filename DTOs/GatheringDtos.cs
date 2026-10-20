namespace ChessApp.Backend.DTOs;

public class CreateGatheringRequest
{
    public string VenueName { get; set; } = null!;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string? Note { get; set; }
}

public class JoinGatheringRequest
{
    public string? Message { get; set; }
}

/// <summary>
/// A person in or asking to join a gathering, with what helps others judge
/// their level. No game results: the app doesn't track games.
/// </summary>
public class GatheringPersonDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string? FullName { get; set; }
    public string? PhotoUrl { get; set; }
    public bool HasBoard { get; set; }
    public int? FideRating { get; set; }
    public int? ChessComRating { get; set; }
    public int? LichessRating { get; set; }
}

public class GatheringDto
{
    public Guid Id { get; set; }
    public GatheringPersonDto Host { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string? Note { get; set; }

    // Members see the venue and its exact point; everyone else sees only a
    // rounded area and a rounded distance.
    public string? VenueName { get; set; }
    public LocationDto? Location { get; set; }
    public LocationDto ApproximateLocation { get; set; } = null!;
    public decimal? DistanceKm { get; set; }

    public List<GatheringPersonDto> Members { get; set; } = new();
    public int MemberCount => Members.Count;

    public bool IsMember { get; set; }
    public bool IsHost { get; set; }

    // The caller's own pending request, if any
    public Guid? MyPendingRequestId { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class GatheringsResponse
{
    public List<GatheringDto> Gatherings { get; set; } = new();
    public int Count => Gatherings.Count;
}

public class JoinRequestDto
{
    public Guid Id { get; set; }
    public Guid GatheringId { get; set; }
    public GatheringPersonDto Requester { get; set; } = null!;
    public string? Message { get; set; }
    public string Status { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class JoinRequestsResponse
{
    public List<JoinRequestDto> Requests { get; set; } = new();
    public int Count => Requests.Count;
}
