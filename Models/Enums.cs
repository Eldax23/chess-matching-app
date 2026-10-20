namespace ChessApp.Backend.Models;

public enum GatheringStatus
{
    Open,      // Only the host so far
    Gathered,  // At least one other member
    Ended,     // Time window passed, or the host ended it
    Cancelled  // Host cancelled before the end
}

public enum JoinRequestStatus
{
    Pending,
    Approved,
    Rejected,  // Every member said no
    Expired,   // Nobody approved in time
    Withdrawn, // Requester withdrew, or it was cancelled because they joined elsewhere
}
