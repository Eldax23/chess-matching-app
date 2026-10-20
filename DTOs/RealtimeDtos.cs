namespace ChessApp.Backend.DTOs;

public class WebSocketMessageDto<T>
{
    public string Type { get; set; } = null!;
    public T Payload { get; set; } = default!;
}

public class ProposalReceivedPayload
{
    public Guid ProposalId { get; set; }
    public Guid ProposerId { get; set; }
    public string ProposerUsername { get; set; } = null!;
    public string? ProposerPhotoUrl { get; set; }
    public string? Message { get; set; }
}

public class ProposalRespondedPayload
{
    public Guid ProposalId { get; set; }
    public string Status { get; set; } = null!;
    public Guid ResponderId { get; set; }
}

public class UserAvailabilityChangedPayload
{
    public Guid UserId { get; set; }
    public bool IsAvailable { get; set; }
    public bool HasBoard { get; set; }
}

public class UserCameOnlinePayload
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = null!;
}

public class JoinRequestReceivedPayload
{
    public Guid RequestId { get; set; }
    public Guid GatheringId { get; set; }
    public GatheringPersonDto Requester { get; set; } = null!;
    public string? Message { get; set; }
}

public class JoinRequestResolvedPayload
{
    public Guid RequestId { get; set; }
    public Guid GatheringId { get; set; }
    public string Status { get; set; } = null!;
}

public class GatheringUpdatedPayload
{
    public Guid GatheringId { get; set; }
    public string Status { get; set; } = null!;
    public int MemberCount { get; set; }
}
