using NetTopologySuite.Geometries;

namespace ChessApp.Backend.Models;

/// <summary>
/// A meetup someone hosts so people nearby can come and play. The app only
/// arranges who meets where and when; it never tracks the games played.
/// </summary>
public class Gathering
{
    public Guid Id { get; set; }

    public Guid HostId { get; set; }
    public virtual User Host { get; set; } = null!;

    // Where: a named venue and its exact point. Only members see the exact point.
    public string VenueName { get; set; } = null!;
    public Point Location { get; set; } = null!;

    // When
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    public string? Note { get; set; }

    public GatheringStatus Status { get; set; } = GatheringStatus.Open;
    public DateTime? EndedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual List<GatheringMember> Members { get; set; } = new();
    public virtual List<JoinRequest> JoinRequests { get; set; } = new();

    public bool IsActive => Status is GatheringStatus.Open or GatheringStatus.Gathered;
}

public class GatheringMember
{
    public Guid GatheringId { get; set; }
    public virtual Gathering Gathering { get; set; } = null!;

    public Guid UserId { get; set; }
    public virtual User User { get; set; } = null!;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A request to join a gathering. One approval from any current member is
/// enough. It is declined only when every member has rejected it, and it
/// expires if nobody approves in time.
/// </summary>
public class JoinRequest
{
    public Guid Id { get; set; }

    public Guid GatheringId { get; set; }
    public virtual Gathering Gathering { get; set; } = null!;

    public Guid RequesterId { get; set; }
    public virtual User Requester { get; set; } = null!;

    public string? Message { get; set; }

    public JoinRequestStatus Status { get; set; } = JoinRequestStatus.Pending;
    public DateTime ExpiresAt { get; set; }

    // Who approved it, when approved
    public Guid? ApprovedById { get; set; }
    public DateTime? RespondedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual List<JoinRequestRejection> Rejections { get; set; } = new();
}

/// <summary>
/// One member's "no" to a join request. Members never see who said no.
/// </summary>
public class JoinRequestRejection
{
    public Guid JoinRequestId { get; set; }
    public virtual JoinRequest JoinRequest { get; set; } = null!;

    public Guid UserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
