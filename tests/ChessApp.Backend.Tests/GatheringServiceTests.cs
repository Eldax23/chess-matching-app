using ChessApp.Backend.Data;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Models;
using ChessApp.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Xunit;

namespace ChessApp.Backend.Tests;

public class GatheringServiceTests
{
    private static readonly GeometryFactory Geometry = new(new PrecisionModel(), 4326);

    private sealed class RecordingNotifier : IGatheringNotifier
    {
        public List<(Guid UserId, string Event)> Sent { get; } = new();

        public Task JoinRequestReceivedAsync(IEnumerable<Guid> memberIds, JoinRequestReceivedPayload payload)
        {
            Sent.AddRange(memberIds.Select(id => (id, "JoinRequestReceived")));
            return Task.CompletedTask;
        }

        public Task JoinRequestResolvedAsync(Guid requesterId, JoinRequestResolvedPayload payload)
        {
            Sent.Add((requesterId, $"JoinRequestResolved:{payload.Status}"));
            return Task.CompletedTask;
        }

        public Task MemberJoinedAsync(IEnumerable<Guid> memberIds, MemberJoinedPayload payload)
        {
            Sent.AddRange(memberIds.Select(id => (id, $"MemberJoined:{payload.Member.Username}")));
            return Task.CompletedTask;
        }

        public Task GatheringUpdatedAsync(IEnumerable<Guid> memberIds, GatheringUpdatedPayload payload)
        {
            Sent.AddRange(memberIds.Select(id => (id, "GatheringUpdated")));
            return Task.CompletedTask;
        }
    }

    private readonly ApplicationDbContext _db;
    private readonly RecordingNotifier _notifier = new();
    private readonly GatheringService _service;

    public GatheringServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new GatheringService(_db, _notifier, NullLogger<GatheringService>.Instance);
    }

    private async Task<User> AddUserAsync(string username)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{username}@example.com",
            Username = username,
            PasswordHash = "x",
            Location = Geometry.CreatePoint(new Coordinate(31.2357, 30.0444))
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private static CreateGatheringRequest Tonight(int startHour = 1, int hours = 2) => new()
    {
        VenueName = "Cafe Riche",
        Latitude = 30.0500,
        Longitude = 31.2400,
        StartsAt = DateTime.UtcNow.AddHours(startHour),
        EndsAt = DateTime.UtcNow.AddHours(startHour + hours),
        Note = "Bringing two boards"
    };

    [Fact]
    public async Task Create_AddsHostAsOnlyMember_AndIsOpen()
    {
        var host = await AddUserAsync("host");

        var gathering = await _service.CreateAsync(host.Id, Tonight());

        Assert.Equal("open", gathering.Status);
        Assert.True(gathering.IsHost);
        Assert.Equal(host.Id, Assert.Single(gathering.Members).Id);
        Assert.Equal("Cafe Riche", gathering.VenueName);
    }

    [Fact]
    public async Task NonMembers_SeeOnlyApproximateLocation()
    {
        var host = await AddUserAsync("host");
        var other = await AddUserAsync("other");
        var created = await _service.CreateAsync(host.Id, Tonight());

        var seen = await _service.GetAsync(created.Id, other.Id);

        Assert.Null(seen.VenueName);
        Assert.Null(seen.Location);
        Assert.Equal(30.05, seen.ApproximateLocation.Latitude);
        Assert.Equal(31.24, seen.ApproximateLocation.Longitude);
        Assert.NotNull(seen.DistanceKm);
        Assert.Equal(0, seen.DistanceKm!.Value % 0.5m);
    }

    [Fact]
    public async Task OneApproval_LetsSomeoneIn_EvenAfterAnotherMemberSaidNo()
    {
        var host = await AddUserAsync("host");
        var friend = await AddUserAsync("friend");
        var newcomer = await AddUserAsync("newcomer");
        var gathering = await _service.CreateAsync(host.Id, Tonight());

        var first = await _service.RequestToJoinAsync(gathering.Id, friend.Id, new JoinGatheringRequest());
        await _service.ApproveAsync(first.Id, host.Id);

        var request = await _service.RequestToJoinAsync(gathering.Id, newcomer.Id, new JoinGatheringRequest());
        var afterNo = await _service.RejectAsync(request.Id, host.Id);
        Assert.Equal("pending", afterNo.Status);

        var approved = await _service.ApproveAsync(request.Id, friend.Id);

        Assert.Equal("approved", approved.Status);
        var seen = await _service.GetAsync(gathering.Id, newcomer.Id);
        Assert.Equal("gathered", seen.Status);
        Assert.Equal(3, seen.MemberCount);
        Assert.Equal("Cafe Riche", seen.VenueName);
    }

    [Fact]
    public async Task Approval_TellsTheOtherMembersWhoJoined()
    {
        var host = await AddUserAsync("host");
        var friend = await AddUserAsync("friend");
        var newcomer = await AddUserAsync("newcomer");
        var gathering = await _service.CreateAsync(host.Id, Tonight());

        var first = await _service.RequestToJoinAsync(gathering.Id, friend.Id, new JoinGatheringRequest());
        await _service.ApproveAsync(first.Id, host.Id);
        var request = await _service.RequestToJoinAsync(gathering.Id, newcomer.Id, new JoinGatheringRequest());
        _notifier.Sent.Clear();

        await _service.ApproveAsync(request.Id, friend.Id);

        var joined = _notifier.Sent.Where(s => s.Event.StartsWith("MemberJoined")).ToList();
        Assert.Equal(new[] { (host.Id, "MemberJoined:newcomer") }, joined);
    }

    [Fact]
    public async Task Request_IsRejected_OnlyWhenEveryMemberSaysNo()
    {
        var host = await AddUserAsync("host");
        var friend = await AddUserAsync("friend");
        var newcomer = await AddUserAsync("newcomer");
        var gathering = await _service.CreateAsync(host.Id, Tonight());
        var first = await _service.RequestToJoinAsync(gathering.Id, friend.Id, new JoinGatheringRequest());
        await _service.ApproveAsync(first.Id, host.Id);

        var request = await _service.RequestToJoinAsync(gathering.Id, newcomer.Id, new JoinGatheringRequest());
        await _service.RejectAsync(request.Id, host.Id);
        var result = await _service.RejectAsync(request.Id, friend.Id);

        Assert.Equal("rejected", result.Status);
        Assert.Contains((newcomer.Id, "JoinRequestResolved:rejected"), _notifier.Sent);
    }

    [Fact]
    public async Task RequestsToApprove_HidesOnesYouAlreadyRejected()
    {
        var host = await AddUserAsync("host");
        var friend = await AddUserAsync("friend");
        var newcomer = await AddUserAsync("newcomer");
        var gathering = await _service.CreateAsync(host.Id, Tonight());
        var first = await _service.RequestToJoinAsync(gathering.Id, friend.Id, new JoinGatheringRequest());
        await _service.ApproveAsync(first.Id, host.Id);
        var request = await _service.RequestToJoinAsync(gathering.Id, newcomer.Id, new JoinGatheringRequest());

        await _service.RejectAsync(request.Id, host.Id);

        Assert.Empty((await _service.GetRequestsToApproveAsync(host.Id)).Requests);
        Assert.Single((await _service.GetRequestsToApproveAsync(friend.Id)).Requests);
    }

    [Fact]
    public async Task BlockedPeople_CantSeeOrJoin()
    {
        var host = await AddUserAsync("host");
        var blocked = await AddUserAsync("blocked");
        var gathering = await _service.CreateAsync(host.Id, Tonight());
        _db.Blocks.Add(new Block { Id = Guid.NewGuid(), BlockerId = host.Id, BlockedId = blocked.Id });
        await _db.SaveChangesAsync();

        var nearby = await _service.GetNearbyAsync(blocked.Id, 30.05, 31.24);

        Assert.Empty(nearby.Gatherings);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.RequestToJoinAsync(gathering.Id, blocked.Id, new JoinGatheringRequest()));
    }

    [Fact]
    public async Task Nearby_LeavesOutGatheringsYoureAlreadyIn()
    {
        var host = await AddUserAsync("host");
        var stranger = await AddUserAsync("stranger");
        var gathering = await _service.CreateAsync(host.Id, Tonight());

        Assert.Empty((await _service.GetNearbyAsync(host.Id, 30.05, 31.24)).Gatherings);
        Assert.Contains((await _service.GetNearbyAsync(stranger.Id, 30.05, 31.24)).Gatherings, g => g.Id == gathering.Id);
    }

    [Fact]
    public async Task JoinRequest_ExpiresAfterFifteenMinutes()
    {
        var host = await AddUserAsync("host");
        var newcomer = await AddUserAsync("newcomer");
        var gathering = await _service.CreateAsync(host.Id, Tonight());

        var request = await _service.RequestToJoinAsync(gathering.Id, newcomer.Id, new JoinGatheringRequest());

        Assert.InRange(request.ExpiresAt - request.CreatedAt,
            TimeSpan.FromMinutes(14.9), TimeSpan.FromMinutes(15.1));
    }

    [Fact]
    public async Task ExpiredRequest_CantBeApproved_AndCanBeAskedAgain()
    {
        var host = await AddUserAsync("host");
        var newcomer = await AddUserAsync("newcomer");
        var gathering = await _service.CreateAsync(host.Id, Tonight());
        var request = await _service.RequestToJoinAsync(gathering.Id, newcomer.Id, new JoinGatheringRequest());

        var stored = await _db.JoinRequests.FindAsync(request.Id);
        stored!.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ApproveAsync(request.Id, host.Id));

        var again = await _service.RequestToJoinAsync(gathering.Id, newcomer.Id, new JoinGatheringRequest());
        Assert.Equal("pending", again.Status);
    }

    [Fact]
    public async Task Joining_WithdrawsOtherRequests_AndCancelsSoloGatherings_AtTheSameTime()
    {
        var hostA = await AddUserAsync("hostA");
        var hostB = await AddUserAsync("hostB");
        var player = await AddUserAsync("player");
        var a = await _service.CreateAsync(hostA.Id, Tonight());
        var b = await _service.CreateAsync(hostB.Id, Tonight());
        var own = await _service.CreateAsync(player.Id, Tonight());

        var toA = await _service.RequestToJoinAsync(a.Id, player.Id, new JoinGatheringRequest());
        var toB = await _service.RequestToJoinAsync(b.Id, player.Id, new JoinGatheringRequest());

        await _service.ApproveAsync(toA.Id, hostA.Id);

        Assert.Equal(JoinRequestStatus.Withdrawn, (await _db.JoinRequests.FindAsync(toB.Id))!.Status);
        Assert.Equal(GatheringStatus.Cancelled, (await _db.Gatherings.FindAsync(own.Id))!.Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.RequestToJoinAsync(b.Id, player.Id, new JoinGatheringRequest()));
    }

    [Fact]
    public async Task Gatherings_HaveNoSizeCap()
    {
        var host = await AddUserAsync("host");
        var gathering = await _service.CreateAsync(host.Id, Tonight());

        for (var i = 0; i < 12; i++)
        {
            var player = await AddUserAsync($"player{i}");
            var request = await _service.RequestToJoinAsync(gathering.Id, player.Id, new JoinGatheringRequest());
            await _service.ApproveAsync(request.Id, host.Id);
        }

        Assert.Equal(13, (await _service.GetAsync(gathering.Id, host.Id)).MemberCount);
    }

    [Fact]
    public async Task LastGuestLeaving_ReopensTheGathering()
    {
        var host = await AddUserAsync("host");
        var guest = await AddUserAsync("guest");
        var gathering = await _service.CreateAsync(host.Id, Tonight());
        var request = await _service.RequestToJoinAsync(gathering.Id, guest.Id, new JoinGatheringRequest());
        await _service.ApproveAsync(request.Id, host.Id);

        await _service.LeaveAsync(gathering.Id, guest.Id);

        Assert.Equal("open", (await _service.GetAsync(gathering.Id, host.Id)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.LeaveAsync(gathering.Id, host.Id));
    }

    [Fact]
    public async Task OnlyTheHost_CanEnd_AndPendingRequestsExpire()
    {
        var host = await AddUserAsync("host");
        var guest = await AddUserAsync("guest");
        var newcomer = await AddUserAsync("newcomer");
        var gathering = await _service.CreateAsync(host.Id, Tonight());
        var first = await _service.RequestToJoinAsync(gathering.Id, guest.Id, new JoinGatheringRequest());
        await _service.ApproveAsync(first.Id, host.Id);
        var pending = await _service.RequestToJoinAsync(gathering.Id, newcomer.Id, new JoinGatheringRequest());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.EndAsync(gathering.Id, guest.Id));
        await _service.EndAsync(gathering.Id, host.Id);

        Assert.Equal("ended", (await _service.GetAsync(gathering.Id, guest.Id)).Status);
        Assert.Equal(JoinRequestStatus.Expired, (await _db.JoinRequests.FindAsync(pending.Id))!.Status);
    }

    [Fact]
    public async Task EndedGathering_SaysWhoYouAlreadyReviewed()
    {
        var host = await AddUserAsync("host");
        var guest = await AddUserAsync("guest");
        var gathering = await _service.CreateAsync(host.Id, Tonight());
        var request = await _service.RequestToJoinAsync(gathering.Id, guest.Id, new JoinGatheringRequest());
        await _service.ApproveAsync(request.Id, host.Id);
        await _service.EndAsync(gathering.Id, host.Id);

        Assert.Empty((await _service.GetAsync(gathering.Id, host.Id)).ReviewedByMe);

        _db.Reviews.Add(new Review { Id = Guid.NewGuid(), GatheringId = gathering.Id, ReviewerId = host.Id, RevieweeId = guest.Id });
        await _db.SaveChangesAsync();

        Assert.Equal(new[] { guest.Id }, (await _service.GetAsync(gathering.Id, host.Id)).ReviewedByMe);
        Assert.Empty((await _service.GetAsync(gathering.Id, guest.Id)).ReviewedByMe);
    }

    [Fact]
    public async Task ExpireStale_EndsFinishedGatherings_AndExpiresRequests()
    {
        var host = await AddUserAsync("host");
        var newcomer = await AddUserAsync("newcomer");
        var gathering = await _service.CreateAsync(host.Id, Tonight());
        var request = await _service.RequestToJoinAsync(gathering.Id, newcomer.Id, new JoinGatheringRequest());

        var storedGathering = await _db.Gatherings.FindAsync(gathering.Id);
        storedGathering!.StartsAt = DateTime.UtcNow.AddHours(-3);
        storedGathering.EndsAt = DateTime.UtcNow.AddMinutes(-1);
        var storedRequest = await _db.JoinRequests.FindAsync(request.Id);
        storedRequest!.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await _service.ExpireStaleAsync();

        Assert.Equal(GatheringStatus.Ended, storedGathering.Status);
        Assert.Equal(JoinRequestStatus.Expired, storedRequest.Status);
        Assert.Contains((newcomer.Id, "JoinRequestResolved:expired"), _notifier.Sent);
    }
}
