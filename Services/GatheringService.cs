using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using ChessApp.Backend.Data;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Models;

namespace ChessApp.Backend.Services;

public interface IGatheringService
{
    Task<GatheringDto> CreateAsync(Guid hostId, CreateGatheringRequest request);

    Task<GatheringsResponse> GetNearbyAsync(Guid userId, double latitude, double longitude, int radiusKm = 10, int limit = 50);

    Task<GatheringsResponse> GetMineAsync(Guid userId);

    Task<GatheringDto> GetAsync(Guid gatheringId, Guid userId);

    Task<JoinRequestDto> RequestToJoinAsync(Guid gatheringId, Guid userId, JoinGatheringRequest request);

    Task<JoinRequestsResponse> GetRequestsToApproveAsync(Guid userId);

    Task<JoinRequestsResponse> GetMyRequestsAsync(Guid userId);

    Task<JoinRequestDto> ApproveAsync(Guid requestId, Guid userId);

    Task<JoinRequestDto> RejectAsync(Guid requestId, Guid userId);

    Task WithdrawAsync(Guid requestId, Guid userId);

    Task LeaveAsync(Guid gatheringId, Guid userId);

    Task EndAsync(Guid gatheringId, Guid userId);

    Task CancelAsync(Guid gatheringId, Guid userId);

    /// <summary>Expires stale join requests and ends gatherings whose time window has passed.</summary>
    Task<int> ExpireStaleAsync();
}

public class GatheringService : IGatheringService
{
    public static readonly TimeSpan JoinRequestLifetime = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _dbContext;
    private readonly IGatheringNotifier _notifier;
    private readonly ILogger<GatheringService> _logger;

    public GatheringService(ApplicationDbContext dbContext, IGatheringNotifier notifier, ILogger<GatheringService> logger)
    {
        _dbContext = dbContext;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<GatheringDto> CreateAsync(Guid hostId, CreateGatheringRequest request)
    {
        var host = await _dbContext.Users.FindAsync(hostId)
            ?? throw new KeyNotFoundException($"User {hostId} not found");

        var hostsOverlapping = await _dbContext.Gatherings.AnyAsync(g =>
            g.HostId == hostId &&
            (g.Status == GatheringStatus.Open || g.Status == GatheringStatus.Gathered) &&
            g.StartsAt < request.EndsAt && request.StartsAt < g.EndsAt);

        if (hostsOverlapping || await IsBusyAsync(hostId, request.StartsAt, request.EndsAt))
            throw new InvalidOperationException("You're already in a gathering at that time");

        var now = DateTime.UtcNow;
        var gathering = new Gathering
        {
            Id = Guid.NewGuid(),
            HostId = hostId,
            VenueName = request.VenueName.Trim(),
            Location = CreatePoint(request.Latitude, request.Longitude),
            StartsAt = request.StartsAt,
            EndsAt = request.EndsAt,
            Note = request.Note,
            Status = GatheringStatus.Open,
            CreatedAt = now,
            UpdatedAt = now
        };
        gathering.Members.Add(new GatheringMember { GatheringId = gathering.Id, UserId = hostId, JoinedAt = now });

        _dbContext.Gatherings.Add(gathering);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Gathering {GatheringId} created by {HostId}", gathering.Id, hostId);

        return await GetAsync(gathering.Id, hostId);
    }

    public async Task<GatheringsResponse> GetNearbyAsync(
        Guid userId, double latitude, double longitude, int radiusKm = 10, int limit = 50)
    {
        var origin = CreatePoint(latitude, longitude);
        var radiusMeters = radiusKm * 1000.0;
        var now = DateTime.UtcNow;

        var gatherings = await ActiveGatherings(now)
            .Where(g => g.Location.Distance(origin) <= radiusMeters)
            .OrderBy(g => g.StartsAt)
            .Take(limit)
            .ToListAsync();

        var blocked = await BlockedUserIdsAsync(userId);
        var myPending = await MyPendingRequestIdsAsync(userId);

        var response = new GatheringsResponse();
        foreach (var gathering in gatherings)
        {
            // Blocks work both ways: hide the gathering if anyone in it is blocked
            if (gathering.Members.Any(m => blocked.Contains(m.UserId)))
                continue;

            // Gatherings you're already in live under "mine", not "nearby"
            if (gathering.Members.Any(m => m.UserId == userId))
                continue;

            response.Gatherings.Add(MapToDto(gathering, userId, origin, myPending));
        }

        return response;
    }

    public async Task<GatheringsResponse> GetMineAsync(Guid userId)
    {
        var since = DateTime.UtcNow.AddDays(-7);

        var gatherings = await WithPeople(_dbContext.Gatherings)
            .Where(g => g.Members.Any(m => m.UserId == userId) &&
                        (g.Status == GatheringStatus.Open || g.Status == GatheringStatus.Gathered || g.EndsAt >= since))
            .OrderByDescending(g => g.StartsAt)
            .ToListAsync();

        var response = new GatheringsResponse();
        foreach (var gathering in gatherings)
            response.Gatherings.Add(MapToDto(gathering, userId, null, new Dictionary<Guid, Guid>()));

        return response;
    }

    public async Task<GatheringDto> GetAsync(Guid gatheringId, Guid userId)
    {
        var gathering = await WithPeople(_dbContext.Gatherings)
            .FirstOrDefaultAsync(g => g.Id == gatheringId)
            ?? throw new KeyNotFoundException($"Gathering {gatheringId} not found");

        var isMember = gathering.Members.Any(m => m.UserId == userId);
        if (!isMember)
        {
            var blocked = await BlockedUserIdsAsync(userId);
            if (gathering.Members.Any(m => blocked.Contains(m.UserId)))
                throw new KeyNotFoundException($"Gathering {gatheringId} not found");
        }

        var viewer = await _dbContext.Users.FindAsync(userId);
        var dto = MapToDto(gathering, userId, viewer?.Location, await MyPendingRequestIdsAsync(userId));
        if (isMember && gathering.Status == GatheringStatus.Ended)
        {
            dto.ReviewedByMe = await _dbContext.Reviews
                .Where(r => r.GatheringId == gatheringId && r.ReviewerId == userId)
                .Select(r => r.RevieweeId)
                .ToListAsync();
        }
        return dto;
    }

    public async Task<JoinRequestDto> RequestToJoinAsync(Guid gatheringId, Guid userId, JoinGatheringRequest request)
    {
        var now = DateTime.UtcNow;
        var gathering = await _dbContext.Gatherings
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == gatheringId)
            ?? throw new KeyNotFoundException($"Gathering {gatheringId} not found");

        var requester = await _dbContext.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException($"User {userId} not found");

        if (!gathering.IsActive || gathering.EndsAt <= now)
            throw new InvalidOperationException("This gathering is no longer taking new people");

        if (gathering.Members.Any(m => m.UserId == userId))
            throw new InvalidOperationException("You're already in this gathering");

        // Anyone in the gathering who has a block either way with the requester
        // means no join. Same message as a missing gathering, so it reveals nothing.
        var blocked = await BlockedUserIdsAsync(userId);
        if (gathering.Members.Any(m => blocked.Contains(m.UserId)))
            throw new KeyNotFoundException($"Gathering {gatheringId} not found");

        var previous = await _dbContext.JoinRequests
            .Where(r => r.GatheringId == gatheringId && r.RequesterId == userId && r.Status == JoinRequestStatus.Pending)
            .ToListAsync();
        if (previous.Any(r => r.ExpiresAt > now))
            throw new InvalidOperationException("You already asked to join this gathering");

        // Expire a lapsed request now rather than waiting for the worker, so the
        // one-pending-request-per-person index lets the new one in
        if (previous.Count > 0)
        {
            foreach (var lapsed in previous)
                lapsed.Status = JoinRequestStatus.Expired;
            await _dbContext.SaveChangesAsync();
        }

        if (await IsBusyAsync(userId, gathering.StartsAt, gathering.EndsAt))
            throw new InvalidOperationException("You're already in a gathering at that time");

        var joinRequest = new JoinRequest
        {
            Id = Guid.NewGuid(),
            GatheringId = gatheringId,
            RequesterId = userId,
            Message = request.Message,
            Status = JoinRequestStatus.Pending,
            ExpiresAt = Min(now + JoinRequestLifetime, gathering.EndsAt),
            CreatedAt = now
        };

        _dbContext.JoinRequests.Add(joinRequest);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User {UserId} asked to join gathering {GatheringId}", userId, gatheringId);

        joinRequest.Requester = requester;
        var dto = MapToDto(joinRequest);

        await _notifier.JoinRequestReceivedAsync(
            gathering.Members.Select(m => m.UserId),
            new JoinRequestReceivedPayload
            {
                RequestId = joinRequest.Id,
                GatheringId = gatheringId,
                Requester = dto.Requester,
                Message = joinRequest.Message
            });

        return dto;
    }

    public async Task<JoinRequestsResponse> GetRequestsToApproveAsync(Guid userId)
    {
        var now = DateTime.UtcNow;

        var requests = await _dbContext.JoinRequests
            .Include(r => r.Requester)
            .Where(r =>
                r.Status == JoinRequestStatus.Pending &&
                r.ExpiresAt > now &&
                r.Gathering.Members.Any(m => m.UserId == userId) &&
                !r.Rejections.Any(x => x.UserId == userId))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync();

        var response = new JoinRequestsResponse();
        foreach (var request in requests)
            response.Requests.Add(MapToDto(request));

        return response;
    }

    public async Task<JoinRequestsResponse> GetMyRequestsAsync(Guid userId)
    {
        var since = DateTime.UtcNow.AddDays(-1);

        var requests = await _dbContext.JoinRequests
            .Include(r => r.Requester)
            .Where(r => r.RequesterId == userId && r.CreatedAt >= since)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var response = new JoinRequestsResponse();
        foreach (var request in requests)
            response.Requests.Add(MapToDto(request));

        return response;
    }

    public async Task<JoinRequestDto> ApproveAsync(Guid requestId, Guid userId)
    {
        var joinRequest = await LoadPendingRequestForMemberAsync(requestId, userId);
        var gathering = joinRequest.Gathering;
        var requesterId = joinRequest.RequesterId;
        var now = DateTime.UtcNow;

        // They may have joined another gathering at the same time since asking
        if (await IsBusyAsync(requesterId, gathering.StartsAt, gathering.EndsAt))
        {
            joinRequest.Status = JoinRequestStatus.Withdrawn;
            joinRequest.RespondedAt = now;
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException("They've already joined another gathering at that time");
        }

        joinRequest.Status = JoinRequestStatus.Approved;
        joinRequest.ApprovedById = userId;
        joinRequest.RespondedAt = now;

        gathering.Members.Add(new GatheringMember { GatheringId = gathering.Id, UserId = requesterId, JoinedAt = now });
        gathering.Status = GatheringStatus.Gathered;
        gathering.UpdatedAt = now;

        // One gathering at a time: drop the requester's other plans that overlap this one
        await ReleaseOverlappingPlansAsync(requesterId, gathering, now);

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Join request {RequestId} approved by {UserId}", requestId, userId);

        await _notifier.JoinRequestResolvedAsync(requesterId, new JoinRequestResolvedPayload
        {
            RequestId = requestId,
            GatheringId = gathering.Id,
            Status = FormatStatus(joinRequest.Status)
        });
        // Tell the rest of the gathering who's coming; the approver and the
        // newcomer already know
        await _notifier.MemberJoinedAsync(
            gathering.Members.Select(m => m.UserId).Where(id => id != requesterId && id != userId),
            new MemberJoinedPayload
            {
                GatheringId = gathering.Id,
                VenueName = gathering.VenueName,
                Member = MapToPerson(joinRequest.Requester),
                MemberCount = gathering.Members.Count
            });
        await NotifyGatheringUpdatedAsync(gathering);

        return MapToDto(joinRequest);
    }

    public async Task<JoinRequestDto> RejectAsync(Guid requestId, Guid userId)
    {
        var joinRequest = await LoadPendingRequestForMemberAsync(requestId, userId);
        var gathering = joinRequest.Gathering;

        if (!joinRequest.Rejections.Any(x => x.UserId == userId))
        {
            joinRequest.Rejections.Add(new JoinRequestRejection
            {
                JoinRequestId = requestId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        // One approval is enough to get in, so it's only declined once everyone says no
        var memberIds = gathering.Members.Select(m => m.UserId).ToHashSet();
        var everyoneSaidNo = memberIds.All(id => joinRequest.Rejections.Any(x => x.UserId == id));
        if (everyoneSaidNo)
        {
            joinRequest.Status = JoinRequestStatus.Rejected;
            joinRequest.RespondedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync();

        if (everyoneSaidNo)
        {
            await _notifier.JoinRequestResolvedAsync(joinRequest.RequesterId, new JoinRequestResolvedPayload
            {
                RequestId = requestId,
                GatheringId = gathering.Id,
                Status = FormatStatus(joinRequest.Status)
            });
        }

        return MapToDto(joinRequest);
    }

    public async Task WithdrawAsync(Guid requestId, Guid userId)
    {
        var joinRequest = await _dbContext.JoinRequests.FindAsync(requestId)
            ?? throw new KeyNotFoundException($"Join request {requestId} not found");

        if (joinRequest.RequesterId != userId)
            throw new UnauthorizedAccessException("Only the person who asked can withdraw a request");

        if (joinRequest.Status != JoinRequestStatus.Pending)
            throw new InvalidOperationException("This request is no longer pending");

        joinRequest.Status = JoinRequestStatus.Withdrawn;
        joinRequest.RespondedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    public async Task LeaveAsync(Guid gatheringId, Guid userId)
    {
        var gathering = await LoadGatheringAsync(gatheringId);

        if (gathering.HostId == userId)
            throw new InvalidOperationException("The host can end or cancel the gathering instead of leaving");

        var member = gathering.Members.FirstOrDefault(m => m.UserId == userId)
            ?? throw new InvalidOperationException("You're not in this gathering");

        if (!gathering.IsActive)
            throw new InvalidOperationException("This gathering is already over");

        gathering.Members.Remove(member);
        if (gathering.Members.Count <= 1)
            gathering.Status = GatheringStatus.Open;
        gathering.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();
        await NotifyGatheringUpdatedAsync(gathering);
    }

    public Task EndAsync(Guid gatheringId, Guid userId) =>
        CloseAsync(gatheringId, userId, GatheringStatus.Ended, JoinRequestStatus.Expired);

    public Task CancelAsync(Guid gatheringId, Guid userId) =>
        CloseAsync(gatheringId, userId, GatheringStatus.Cancelled, JoinRequestStatus.Withdrawn);

    public async Task<int> ExpireStaleAsync()
    {
        var now = DateTime.UtcNow;

        var staleRequests = await _dbContext.JoinRequests
            .Where(r => r.Status == JoinRequestStatus.Pending && r.ExpiresAt <= now)
            .ToListAsync();
        foreach (var request in staleRequests)
            request.Status = JoinRequestStatus.Expired;

        var finished = await _dbContext.Gatherings
            .Where(g => (g.Status == GatheringStatus.Open || g.Status == GatheringStatus.Gathered) && g.EndsAt <= now)
            .ToListAsync();
        foreach (var gathering in finished)
        {
            gathering.Status = GatheringStatus.Ended;
            gathering.EndedAt = gathering.EndsAt;
            gathering.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync();

        foreach (var request in staleRequests)
        {
            await _notifier.JoinRequestResolvedAsync(request.RequesterId, new JoinRequestResolvedPayload
            {
                RequestId = request.Id,
                GatheringId = request.GatheringId,
                Status = FormatStatus(request.Status)
            });
        }

        return staleRequests.Count + finished.Count;
    }

    // =====================================================
    // HELPERS
    // =====================================================

    private async Task CloseAsync(Guid gatheringId, Guid userId, GatheringStatus status, JoinRequestStatus pendingRequestsBecome)
    {
        var gathering = await LoadGatheringAsync(gatheringId);

        if (gathering.HostId != userId)
            throw new UnauthorizedAccessException("Only the host can do this");

        if (!gathering.IsActive)
            throw new InvalidOperationException("This gathering is already over");

        var now = DateTime.UtcNow;
        gathering.Status = status;
        gathering.EndedAt = now;
        gathering.UpdatedAt = now;

        var pending = await _dbContext.JoinRequests
            .Where(r => r.GatheringId == gatheringId && r.Status == JoinRequestStatus.Pending)
            .ToListAsync();
        foreach (var request in pending)
        {
            request.Status = pendingRequestsBecome;
            request.RespondedAt = now;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Gathering {GatheringId} {Status} by host", gatheringId, status);

        await NotifyGatheringUpdatedAsync(gathering);
    }

    private async Task<Gathering> LoadGatheringAsync(Guid gatheringId) =>
        await _dbContext.Gatherings
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == gatheringId)
        ?? throw new KeyNotFoundException($"Gathering {gatheringId} not found");

    private async Task<JoinRequest> LoadPendingRequestForMemberAsync(Guid requestId, Guid userId)
    {
        var joinRequest = await _dbContext.JoinRequests
            .Include(r => r.Requester)
            .Include(r => r.Rejections)
            .Include(r => r.Gathering)
            .ThenInclude(g => g.Members)
            .FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new KeyNotFoundException($"Join request {requestId} not found");

        if (!joinRequest.Gathering.Members.Any(m => m.UserId == userId))
            throw new UnauthorizedAccessException("Only people in the gathering can answer this request");

        if (joinRequest.Status != JoinRequestStatus.Pending)
            throw new InvalidOperationException("This request is no longer pending");

        if (joinRequest.ExpiresAt <= DateTime.UtcNow)
        {
            joinRequest.Status = JoinRequestStatus.Expired;
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException("This request has expired");
        }

        if (!joinRequest.Gathering.IsActive)
            throw new InvalidOperationException("This gathering is already over");

        return joinRequest;
    }

    /// <summary>
    /// Whether the user is already in an active gathering whose time window overlaps the given one.
    /// </summary>
    private Task<bool> IsBusyAsync(Guid userId, DateTime startsAt, DateTime endsAt) =>
        _dbContext.GatheringMembers.AnyAsync(m =>
            m.UserId == userId &&
            (m.Gathering.Status == GatheringStatus.Open || m.Gathering.Status == GatheringStatus.Gathered) &&
            m.Gathering.StartsAt < endsAt && startsAt < m.Gathering.EndsAt &&
            // A gathering they host alone doesn't count: it's released when they join elsewhere
            !(m.Gathering.HostId == userId && m.Gathering.Status == GatheringStatus.Open));

    /// <summary>
    /// After someone joins a gathering, withdraw their other pending requests and
    /// cancel the solo gatherings they host that overlap it.
    /// </summary>
    private async Task ReleaseOverlappingPlansAsync(Guid userId, Gathering joined, DateTime now)
    {
        var otherRequests = await _dbContext.JoinRequests
            .Where(r =>
                r.RequesterId == userId &&
                r.Status == JoinRequestStatus.Pending &&
                r.GatheringId != joined.Id &&
                r.Gathering.StartsAt < joined.EndsAt && joined.StartsAt < r.Gathering.EndsAt)
            .ToListAsync();
        foreach (var request in otherRequests)
        {
            request.Status = JoinRequestStatus.Withdrawn;
            request.RespondedAt = now;
        }

        var soloGatherings = await _dbContext.Gatherings
            .Where(g =>
                g.HostId == userId &&
                g.Id != joined.Id &&
                g.Status == GatheringStatus.Open &&
                g.StartsAt < joined.EndsAt && joined.StartsAt < g.EndsAt)
            .ToListAsync();
        foreach (var gathering in soloGatherings)
        {
            gathering.Status = GatheringStatus.Cancelled;
            gathering.EndedAt = now;
            gathering.UpdatedAt = now;

            var requestsToIt = await _dbContext.JoinRequests
                .Where(r => r.GatheringId == gathering.Id && r.Status == JoinRequestStatus.Pending)
                .ToListAsync();
            foreach (var request in requestsToIt)
            {
                request.Status = JoinRequestStatus.Withdrawn;
                request.RespondedAt = now;
            }
        }
    }

    private IQueryable<Gathering> ActiveGatherings(DateTime now) =>
        WithPeople(_dbContext.Gatherings)
            .Where(g => (g.Status == GatheringStatus.Open || g.Status == GatheringStatus.Gathered) && g.EndsAt > now);

    private static IQueryable<Gathering> WithPeople(IQueryable<Gathering> query) =>
        query
            .Include(g => g.Host)
            .Include(g => g.Members)
            .ThenInclude(m => m.User);

    private async Task<HashSet<Guid>> BlockedUserIdsAsync(Guid userId)
    {
        var ids = await _dbContext.Blocks
            .Where(b => b.BlockerId == userId || b.BlockedId == userId)
            .Select(b => b.BlockerId == userId ? b.BlockedId : b.BlockerId)
            .ToListAsync();
        return ids.ToHashSet();
    }

    private async Task<Dictionary<Guid, Guid>> MyPendingRequestIdsAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var pending = await _dbContext.JoinRequests
            .Where(r => r.RequesterId == userId && r.Status == JoinRequestStatus.Pending && r.ExpiresAt > now)
            .Select(r => new { r.GatheringId, r.Id })
            .ToListAsync();
        return pending.ToDictionary(r => r.GatheringId, r => r.Id);
    }

    private Task NotifyGatheringUpdatedAsync(Gathering gathering) =>
        _notifier.GatheringUpdatedAsync(
            gathering.Members.Select(m => m.UserId),
            new GatheringUpdatedPayload
            {
                GatheringId = gathering.Id,
                Status = FormatStatus(gathering.Status),
                MemberCount = gathering.Members.Count
            });

    private static Point CreatePoint(double latitude, double longitude)
    {
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        return factory.CreatePoint(new Coordinate(longitude, latitude));
    }

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string FormatStatus<T>(T status) where T : Enum => status.ToString().ToLowerInvariant();

    private static GatheringDto MapToDto(
        Gathering gathering, Guid viewerId, Point? viewerLocation, IReadOnlyDictionary<Guid, Guid> myPendingRequests)
    {
        var isMember = gathering.Members.Any(m => m.UserId == viewerId);

        return new GatheringDto
        {
            Id = gathering.Id,
            Host = MapToPerson(gathering.Host),
            Status = FormatStatus(gathering.Status),
            StartsAt = gathering.StartsAt,
            EndsAt = gathering.EndsAt,
            Note = gathering.Note,
            VenueName = isMember ? gathering.VenueName : null,
            Location = isMember
                ? new LocationDto { Latitude = gathering.Location.Y, Longitude = gathering.Location.X }
                : null,
            ApproximateLocation = new LocationDto
            {
                // Two decimals is roughly a 1 km grid
                Latitude = Math.Round(gathering.Location.Y, 2),
                Longitude = Math.Round(gathering.Location.X, 2)
            },
            DistanceKm = viewerLocation != null ? RoundedDistance(viewerLocation, gathering.Location) : null,
            Members = gathering.Members
                .OrderBy(m => m.JoinedAt)
                .Select(m => MapToPerson(m.User))
                .ToList(),
            IsMember = isMember,
            IsHost = gathering.HostId == viewerId,
            MyPendingRequestId = myPendingRequests.TryGetValue(gathering.Id, out var requestId) ? requestId : null,
            CreatedAt = gathering.CreatedAt
        };
    }

    /// <summary>
    /// Distance rounded up to the next half kilometre, so it can't be used to pin down the venue.
    /// </summary>
    private static decimal RoundedDistance(Point from, Point to)
    {
        var km = GeoDistance.Kilometers(from, to);
        return (decimal)(Math.Ceiling(km * 2) / 2);
    }

    private static JoinRequestDto MapToDto(JoinRequest request) => new()
    {
        Id = request.Id,
        GatheringId = request.GatheringId,
        Requester = MapToPerson(request.Requester),
        Message = request.Message,
        Status = FormatStatus(request.Status),
        ExpiresAt = request.ExpiresAt,
        CreatedAt = request.CreatedAt
    };

    private static GatheringPersonDto MapToPerson(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        FullName = user.FullName,
        PhotoUrl = user.PhotoUrl,
        HasBoard = user.HasBoard,
        FideRating = user.FideRating,
        ChessComRating = user.ChessComRating,
        LichessRating = user.LichessRating
    };
}
