using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using ChessApp.Backend.Data;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Models;

namespace ChessApp.Backend.Services;

public interface IProposalService
{
    Task<ProposalResponse> CreateProposalAsync(Guid proposerId, CreateProposalRequest request);

    Task<IncomingProposalsResponse> GetIncomingProposalsAsync(Guid userId, string status = "pending", int limit = 20);

    Task<OutgoingProposalsResponse> GetOutgoingProposalsAsync(Guid userId, string? status = null, int limit = 20);

    Task<AcceptProposalResponse> AcceptProposalAsync(Guid proposalId, Guid userId, AcceptProposalRequest request);

    Task<RejectProposalResponse> RejectProposalAsync(Guid proposalId, Guid userId, RejectProposalRequest request);

    Task CancelProposalAsync(Guid proposalId, Guid userId);

    Task<Guid?> GetProposerIdAsync(Guid proposalId);
}

public class ProposalService : IProposalService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<ProposalService> _logger;

    public ProposalService(ApplicationDbContext dbContext, ILogger<ProposalService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ProposalResponse> CreateProposalAsync(Guid proposerId, CreateProposalRequest request)
    {
        var proposer = await _dbContext.Users.FindAsync(proposerId);
        if (proposer == null)
            throw new KeyNotFoundException($"Proposer {proposerId} not found");

        var receiver = await _dbContext.Users.FindAsync(request.ReceiverId);
        if (receiver == null)
            throw new KeyNotFoundException($"Receiver {request.ReceiverId} not found");

        // Check if they're blocking each other
        var isBlocked = await _dbContext.Blocks.AnyAsync(b =>
            (b.BlockerId == proposerId && b.BlockedId == request.ReceiverId) ||
            (b.BlockerId == request.ReceiverId && b.BlockedId == proposerId));

        if (isBlocked)
            throw new InvalidOperationException("Cannot propose to a blocked user");

        // Check if receiver is available
        if (!receiver.IsAvailable || receiver.AvailabilityExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("Receiver is not currently available");

        // Check for duplicate pending proposals
        var existingProposal = await _dbContext.Proposals.FirstOrDefaultAsync(p =>
            p.ProposerId == proposerId &&
            p.ReceiverId == request.ReceiverId &&
            p.Status == ProposalStatus.Pending &&
            p.ExpiresAt > DateTime.UtcNow);

        if (existingProposal != null)
            throw new InvalidOperationException("You already have a pending proposal to this user");

        // Create meeting location point
        var geometryFactory = new GeometryFactory(
            new NetTopologySuite.Geometries.PrecisionModel(), 4326);
        var meetingLocation = geometryFactory.CreatePoint(
            new NetTopologySuite.Geometries.Coordinate(request.MeetingLongitude, request.MeetingLatitude));

        var proposal = new Proposal
        {
            Id = Guid.NewGuid(),
            ProposerId = proposerId,
            ReceiverId = request.ReceiverId,
            Message = request.Message,
            MeetingLocation = meetingLocation,
            Status = ProposalStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Proposals.Add(proposal);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Proposal created: {ProposalId} from {ProposerId} to {ReceiverId} at location ({Lat},{Lon})",
            proposal.Id, proposerId, request.ReceiverId, request.MeetingLatitude, request.MeetingLongitude);

        return MapToProposalResponse(proposal);
    }

    public async Task<IncomingProposalsResponse> GetIncomingProposalsAsync(
        Guid userId, string status = "pending", int limit = 20)
    {
        var currentUser = await _dbContext.Users.FindAsync(userId);
        if (currentUser == null)
            throw new KeyNotFoundException($"User {userId} not found");

        var proposals = await _dbContext.Proposals
            .Include(p => p.Proposer)
            .ThenInclude(u => u.Stats)
            .Where(p =>
                p.ReceiverId == userId &&
                (status == "pending" ? p.Status == ProposalStatus.Pending :
                 status == "accepted" ? p.Status == ProposalStatus.Accepted :
                 status == "rejected" ? p.Status == ProposalStatus.Rejected : true))
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync();

        var response = new IncomingProposalsResponse();
        foreach (var proposal in proposals)
        {
            response.Proposals.Add(MapToProposalWithDistanceDto(proposal, currentUser.Location));
        }

        return response;
    }

    public async Task<OutgoingProposalsResponse> GetOutgoingProposalsAsync(
        Guid userId, string? status = null, int limit = 20)
    {
        var query = _dbContext.Proposals
            .Include(p => p.Receiver)
            .ThenInclude(u => u.Stats)
            .Where(p => p.ProposerId == userId);

        if (!string.IsNullOrEmpty(status))
        {
            var parsedStatus = Enum.Parse<ProposalStatus>(status, ignoreCase: true);
            query = query.Where(p => p.Status == parsedStatus);
        }

        var proposals = await query
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .ToListAsync();

        var response = new OutgoingProposalsResponse();
        foreach (var proposal in proposals)
        {
            response.Proposals.Add(MapToOutgoingProposalDetailResponse(proposal));
        }

        return response;
    }

    public async Task<AcceptProposalResponse> AcceptProposalAsync(
        Guid proposalId, Guid userId, AcceptProposalRequest request)
    {
        var proposal = await _dbContext.Proposals
            .Include(p => p.Proposer)
            .FirstOrDefaultAsync(p => p.Id == proposalId);

        if (proposal == null)
            throw new KeyNotFoundException($"Proposal {proposalId} not found");

        if (proposal.ReceiverId != userId)
            throw new UnauthorizedAccessException("Only the receiver can accept this proposal");

        if (proposal.Status != ProposalStatus.Pending)
            throw new InvalidOperationException($"Cannot accept proposal with status {proposal.Status}");

        if (proposal.ExpiresAt <= DateTime.UtcNow)
        {
            proposal.Status = ProposalStatus.Expired;
            await _dbContext.SaveChangesAsync();
            throw new InvalidOperationException("Proposal has expired");
        }

        if (proposal.MeetingLocation == null)
            throw new InvalidOperationException("Proposal does not have a meeting location set");

        // Create match record using the proposal's meeting location
        var match = new Match
        {
            Id = Guid.NewGuid(),
            Player1Id = proposal.ProposerId,
            Player2Id = proposal.ReceiverId,
            ProposalId = proposalId,
            PlayedAt = DateTime.UtcNow,
            Location = proposal.MeetingLocation, // Use proposal's meeting location
            Outcome = MatchOutcome.NotPlayed,
            CreatedAt = DateTime.UtcNow
        };

        proposal.Status = ProposalStatus.Accepted;
        proposal.RespondedAt = DateTime.UtcNow;
        proposal.MatchId = match.Id;

        _dbContext.Matches.Add(match);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Proposal {ProposalId} accepted. Match {MatchId} created at meeting location",
            proposalId, match.Id);

        return new AcceptProposalResponse
        {
            Id = proposal.Id,
            Status = proposal.Status.ToString().ToLowerInvariant(),
            RespondedAt = proposal.RespondedAt.Value,
            MatchId = match.Id
        };
    }

    public async Task<RejectProposalResponse> RejectProposalAsync(
        Guid proposalId, Guid userId, RejectProposalRequest request)
    {
        var proposal = await _dbContext.Proposals.FindAsync(proposalId);

        if (proposal == null)
            throw new KeyNotFoundException($"Proposal {proposalId} not found");

        if (proposal.ReceiverId != userId)
            throw new UnauthorizedAccessException("Only the receiver can reject this proposal");

        if (proposal.Status != ProposalStatus.Pending)
            throw new InvalidOperationException($"Cannot reject proposal with status {proposal.Status}");

        proposal.Status = ProposalStatus.Rejected;
        proposal.RespondedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Proposal {ProposalId} rejected", proposalId);

        return new RejectProposalResponse
        {
            Id = proposal.Id,
            Status = proposal.Status.ToString().ToLowerInvariant(),
            RespondedAt = proposal.RespondedAt.Value
        };
    }

    public async Task CancelProposalAsync(Guid proposalId, Guid userId)
    {
        var proposal = await _dbContext.Proposals.FindAsync(proposalId);

        if (proposal == null)
            throw new KeyNotFoundException($"Proposal {proposalId} not found");

        if (proposal.ProposerId != userId)
            throw new UnauthorizedAccessException("Only the proposer can cancel");

        if (proposal.Status != ProposalStatus.Pending)
            throw new InvalidOperationException($"Cannot cancel proposal with status {proposal.Status}");

        proposal.Status = ProposalStatus.Cancelled;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Proposal {ProposalId} cancelled", proposalId);
    }

    public async Task<Guid?> GetProposerIdAsync(Guid proposalId)
    {
        return await _dbContext.Proposals
            .Where(p => p.Id == proposalId)
            .Select(p => (Guid?)p.ProposerId)
            .FirstOrDefaultAsync();
    }

    private ProposalResponse MapToProposalResponse(Proposal proposal)
    {
        return new ProposalResponse
        {
            Id = proposal.Id,
            ProposerId = proposal.ProposerId,
            ReceiverId = proposal.ReceiverId,
            Status = proposal.Status.ToString().ToLowerInvariant(),
            Message = proposal.Message,
            MeetingLocation = proposal.MeetingLocation != null ? new LocationDto
            {
                Latitude = proposal.MeetingLocation.Coordinate.Y,
                Longitude = proposal.MeetingLocation.Coordinate.X
            } : null,
            ExpiresAt = proposal.ExpiresAt,
            CreatedAt = proposal.CreatedAt
        };
    }

    private ProposalDetailResponse MapToProposalDetailResponse(Proposal proposal)
    {
        return new ProposalDetailResponse
        {
            Id = proposal.Id,
            Proposer = MapToPublicProfile(proposal.Proposer),
            Status = proposal.Status.ToString().ToLowerInvariant(),
            Message = proposal.Message,
            ExpiresAt = proposal.ExpiresAt,
            CreatedAt = proposal.CreatedAt
        };
    }

    private ProposalDetailResponse MapToOutgoingProposalDetailResponse(Proposal proposal)
    {
        return new ProposalDetailResponse
        {
            Id = proposal.Id,
            Proposer = MapToPublicProfile(proposal.Receiver),
            Status = proposal.Status.ToString().ToLowerInvariant(),
            Message = proposal.Message,
            ExpiresAt = proposal.ExpiresAt,
            CreatedAt = proposal.CreatedAt
        };
    }

    private ProposalWithDistanceDto MapToProposalWithDistanceDto(Proposal proposal, NetTopologySuite.Geometries.Point? userLocation)
    {
        var distance = 0.0m;

        // Calculate distance from user to proposal's meeting location if both exist
        if (userLocation != null && proposal.MeetingLocation != null)
        {
            distance = (decimal)Math.Round(GeoDistance.Kilometers(userLocation, proposal.MeetingLocation), 2);
        }

        return new ProposalWithDistanceDto
        {
            Id = proposal.Id,
            Proposer = MapToPublicProfile(proposal.Proposer),
            Status = proposal.Status.ToString().ToLowerInvariant(),
            Message = proposal.Message,
            MeetingLocation = proposal.MeetingLocation != null ? new LocationDto
            {
                Latitude = proposal.MeetingLocation.Coordinate.Y,
                Longitude = proposal.MeetingLocation.Coordinate.X
            } : null,
            DistanceFromYouKm = distance,
            ExpiresAt = proposal.ExpiresAt,
            CreatedAt = proposal.CreatedAt
        };
    }

    private UserPublicProfileResponse MapToPublicProfile(User user)
    {
        return new UserPublicProfileResponse
        {
            Id = user.Id,
            Username = user.Username,
            FullName = user.FullName,
            PhotoUrl = user.PhotoUrl,
            Bio = user.Bio,
            HasBoard = user.HasBoard,
            FideRating = user.FideRating,
            ChessComRating = user.ChessComRating,
            LichessRating = user.LichessRating,
            Stats = user.Stats != null ? MapToStatsDto(user.Stats) : null
        };
    }

    private UserStatsDto MapToStatsDto(UserStats stats)
    {
        return new UserStatsDto
        {
            TotalMatchesPlayed = stats.TotalMatchesPlayed,
            TotalWins = stats.TotalWins,
            TotalLosses = stats.TotalLosses,
            TotalDraws = stats.TotalDraws,
            WinRate = stats.WinRate,
            AvgOpponentRating = stats.AvgOpponentRating
        };
    }
}
