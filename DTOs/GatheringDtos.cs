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
    // People you've already reviewed for this gathering (only filled in on the detail view)
    public List<Guid> ReviewedByMe { get; set; } = new();
    // When the latest chat message was sent (members only, detail view only)
    public DateTime? LastMessageAt { get; set; }
    // When the latest chat message you've seen was sent (members only, detail view only)
    public DateTime? MyChatReadUpTo { get; set; }

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

public class SendGatheringMessageRequest
{
    public string Body { get; set; } = null!;
}

public class GatheringMessageDto
{
    public Guid Id { get; set; }
    public Guid GatheringId { get; set; }
    public GatheringPersonDto Sender { get; set; } = null!;
    public string Body { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}

public class GatheringMessagesResponse
{
    // Oldest first, so a page can be shown as is
    public List<GatheringMessageDto> Messages { get; set; } = new();
    public int Count => Messages.Count;
    // Whether there are older messages before this page
    public bool HasMore { get; set; }
    // Whether the caller can still post (the gathering is active)
    public bool CanSend { get; set; }
    // How far each member has received and read, for delivered/read ticks
    public List<ChatReceiptDto> Receipts { get; set; } = new();
}

public class ChatReceiptDto
{
    public GatheringPersonDto Member { get; set; } = null!;
    public DateTime? DeliveredUpTo { get; set; }
    public DateTime? ReadUpTo { get; set; }
}

/// <summary>
/// Moves your receipts forward: pass the createdAt of the newest message that
/// reached you and/or that you've seen. Reading implies delivery.
/// </summary>
public class UpdateChatReceiptRequest
{
    public DateTime? DeliveredUpTo { get; set; }
    public DateTime? ReadUpTo { get; set; }
}
