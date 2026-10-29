using Microsoft.AspNetCore.SignalR;
using ChessApp.Backend.DTOs;

namespace ChessApp.Backend.Services;

/// <summary>
/// Real-time events for gatherings. Each user's connections join the
/// "user-{id}" group in NotificationHub, so events reach every device they use.
/// </summary>
public interface IGatheringNotifier
{
    Task JoinRequestReceivedAsync(IEnumerable<Guid> memberIds, JoinRequestReceivedPayload payload);

    Task JoinRequestResolvedAsync(Guid requesterId, JoinRequestResolvedPayload payload);

    Task MemberJoinedAsync(IEnumerable<Guid> memberIds, MemberJoinedPayload payload);

    Task GatheringUpdatedAsync(IEnumerable<Guid> memberIds, GatheringUpdatedPayload payload);
}

public class HubGatheringNotifier : IGatheringNotifier
{
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<HubGatheringNotifier> _logger;

    public HubGatheringNotifier(IHubContext<NotificationHub> hubContext, ILogger<HubGatheringNotifier> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task JoinRequestReceivedAsync(IEnumerable<Guid> memberIds, JoinRequestReceivedPayload payload) =>
        SendAsync(memberIds, "JoinRequestReceived", payload);

    public Task JoinRequestResolvedAsync(Guid requesterId, JoinRequestResolvedPayload payload) =>
        SendAsync(new[] { requesterId }, "JoinRequestResolved", payload);

    public Task MemberJoinedAsync(IEnumerable<Guid> memberIds, MemberJoinedPayload payload) =>
        SendAsync(memberIds, "MemberJoined", payload);

    public Task GatheringUpdatedAsync(IEnumerable<Guid> memberIds, GatheringUpdatedPayload payload) =>
        SendAsync(memberIds, "GatheringUpdated", payload);

    private async Task SendAsync(IEnumerable<Guid> userIds, string method, object payload)
    {
        try
        {
            var groups = userIds.Select(id => $"user-{id}").ToList();
            await _hubContext.Clients.Groups(groups).SendAsync(method, payload);
        }
        catch (Exception ex)
        {
            // Real-time delivery is best effort; the change is already saved
            _logger.LogWarning(ex, "Failed to send {Method} notification", method);
        }
    }
}
