namespace ChessApp.Backend.DTOs;

public class WebSocketMessageDto<T>
{
    public string Type { get; set; } = null!;
    public T Payload { get; set; } = default!;
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

public class GatheringChatReceiptPayload
{
    public Guid GatheringId { get; set; }
    public Guid UserId { get; set; }
    public DateTime? DeliveredUpTo { get; set; }
    public DateTime? ReadUpTo { get; set; }
}

public class GatheringMessageReceivedPayload
{
    public GatheringMessageDto Message { get; set; } = null!;
}
