using System;
using Microsoft.AspNetCore.SignalR;
using ChessApp.Backend.DTOs;

namespace ChessApp.Backend.Services;

public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;
    private static Dictionary<Guid, string> UserConnections = new();

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        // Extract user ID from JWT token (Context.User claims)
        var userIdClaim = Context.User?.FindFirst("sub")?.Value ??
                         Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (Guid.TryParse(userIdClaim, out var userId))
        {
            UserConnections[userId] = Context.ConnectionId;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");

            _logger.LogInformation("User {UserId} connected. Connection: {ConnectionId}",
                userId, Context.ConnectionId);

            // Broadcast user online status
            await Clients.AllExcept(Context.ConnectionId)
                .SendAsync("UserCameOnline", new UserCameOnlinePayload
                {
                    UserId = userId,
                    Username = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? "Unknown"
                });
        }
        else
        {
            _logger.LogWarning("Connection without valid user ID");
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userIdClaim = Context.User?.FindFirst("sub")?.Value ??
                         Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (Guid.TryParse(userIdClaim, out var userId))
        {
            UserConnections.Remove(userId);
            _logger.LogInformation("User {UserId} disconnected", userId);

            // Broadcast user offline status
            await Clients.All.SendAsync("UserWentOffline", new { userId });
        }

        await base.OnDisconnectedAsync(exception);
    }

    // =====================================================
    // PUBLIC METHODS (can be called from clients)
    // =====================================================

    public async Task UpdateAvailability(bool isAvailable)
    {
        var userIdClaim = Context.User?.FindFirst("sub")?.Value ??
                         Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            await Clients.Caller.SendAsync("Error", "Unauthorized");
            return;
        }

        var payload = new UserAvailabilityChangedPayload
        {
            UserId = userId,
            IsAvailable = isAvailable,
            HasBoard = isAvailable // You might want to track this separately
        };

        // Broadcast to all clients
        await Clients.All.SendAsync("UserAvailabilityChanged", payload);

        _logger.LogInformation("User {UserId} availability updated: {IsAvailable}",
            userId, isAvailable);
    }

    // Called by server/backend to notify user of incoming proposal
    public async Task NotifyProposalReceived(Guid userId, ProposalReceivedPayload payload)
    {
        var connectionId = GetUserConnectionId(userId);
        if (connectionId != null)
        {
            await Clients.Client(connectionId)
                .SendAsync("ProposalReceived", payload);
        }
    }

    // Called by server to notify user of proposal response
    public async Task NotifyProposalResponded(Guid userId, ProposalRespondedPayload payload)
    {
        var connectionId = GetUserConnectionId(userId);
        if (connectionId != null)
        {
            await Clients.Client(connectionId)
                .SendAsync("ProposalResponded", payload);
        }
    }

    // Called by server for general announcements
    public async Task BroadcastMessage(string message)
    {
        await Clients.All.SendAsync("MessageReceived", new { message, sentAt = DateTime.UtcNow });
    }

    // =====================================================
    // HELPER METHODS
    // =====================================================

    private string? GetUserConnectionId(Guid userId)
    {
        UserConnections.TryGetValue(userId, out var connectionId);
        return connectionId;
    }

    public static async Task SendProposalNotification(
        IHubContext<NotificationHub> hubContext,
        Guid receiverId,
        ProposalReceivedPayload payload)
    {
        if (UserConnections.TryGetValue(receiverId, out var connectionId))
        {
            await hubContext.Clients.Client(connectionId)
                .SendAsync("ProposalReceived", payload);
        }
    }

    public static async Task SendProposalRespondedNotification(
        IHubContext<NotificationHub> hubContext,
        Guid proposerId,
        ProposalRespondedPayload payload)
    {
        if (UserConnections.TryGetValue(proposerId, out var connectionId))
        {
            await hubContext.Clients.Client(connectionId)
                .SendAsync("ProposalResponded", payload);
        }
    }

    public static async Task BroadcastAvailabilityChange(
        IHubContext<NotificationHub> hubContext,
        UserAvailabilityChangedPayload payload)
    {
        await hubContext.Clients.All.SendAsync("UserAvailabilityChanged", payload);
    }
}
