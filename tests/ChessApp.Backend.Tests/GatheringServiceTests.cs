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

        public Task GatheringUpdatedAsync(IEnumerable<Guid> memberIds, GatheringUpdatedPayload payload)
        {
            Sent.AddRange(memberIds.Select(id => (id, "GatheringUpdated")));
            return Task.CompletedTask;
        }

        public Task GatheringMessageReceivedAsync(IEnumerable<Guid> memberIds, GatheringMessageReceivedPayload payload)
        {
            Sent.AddRange(memberIds.Select(id => (id, $"GatheringMessageReceived:{payload.Message.Body}")));
            return Task.CompletedTask;
        }

        public Task GatheringChatReceiptAsync(IEnumerable<Guid> memberIds, GatheringChatReceiptPayload payload)
        {
            Sent.AddRange(memberIds.Select(id => (id, $"GatheringChatReceipt:{payload.UserId}")));
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

    // ===== CHAT =====

    private async Task<(User Host, User Guest, GatheringDto Gathering)> GatheringWithGuestAsync()
    {
        var host = await AddUserAsync("host");
        var guest = await AddUserAsync("guest");
        var gathering = await _service.CreateAsync(host.Id, Tonight());
        var request = await _service.RequestToJoinAsync(gathering.Id, guest.Id, new JoinGatheringRequest());
        await _service.ApproveAsync(request.Id, host.Id);
        return (host, guest, gathering);
    }

    private static SendGatheringMessageRequest Say(string body) => new() { Body = body };

    [Fact]
    public async Task Members_CanChat_AndEveryMemberIsNotified()
    {
        var (host, guest, gathering) = await GatheringWithGuestAsync();
        _notifier.Sent.Clear();

        var sent = await _service.SendMessageAsync(gathering.Id, guest.Id, Say("  On my way!  "));

        Assert.Equal("On my way!", sent.Body);
        Assert.Equal(0, sent.CreatedAt.Ticks % 10); // microseconds, as Postgres stores it
        Assert.Equal(guest.Id, sent.Sender.Id);
        Assert.Contains((host.Id, "GatheringMessageReceived:On my way!"), _notifier.Sent);
        Assert.Contains((guest.Id, "GatheringMessageReceived:On my way!"), _notifier.Sent);

        var page = await _service.GetMessagesAsync(gathering.Id, host.Id, before: null);
        Assert.Equal(new[] { "On my way!" }, page.Messages.Select(m => m.Body));
        Assert.True(page.CanSend);
        Assert.False(page.HasMore);
        Assert.Equal(sent.CreatedAt, (await _service.GetAsync(gathering.Id, host.Id)).LastMessageAt);
    }

    [Fact]
    public async Task NonMembers_CannotReadOrPost()
    {
        var (_, _, gathering) = await GatheringWithGuestAsync();
        var outsider = await AddUserAsync("outsider");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.GetMessagesAsync(gathering.Id, outsider.Id, before: null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.SendMessageAsync(gathering.Id, outsider.Id, Say("hi")));
        Assert.Null((await _service.GetAsync(gathering.Id, outsider.Id)).LastMessageAt);
    }

    [Fact]
    public async Task BlankMessages_AreRejected()
    {
        var (host, _, gathering) = await GatheringWithGuestAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendMessageAsync(gathering.Id, host.Id, Say("   ")));
    }

    [Fact]
    public async Task EndedGathering_ChatIsReadOnly()
    {
        var (host, guest, gathering) = await GatheringWithGuestAsync();
        await _service.SendMessageAsync(gathering.Id, guest.Id, Say("Good games"));
        await _service.EndAsync(gathering.Id, host.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendMessageAsync(gathering.Id, guest.Id, Say("One more?")));

        var page = await _service.GetMessagesAsync(gathering.Id, guest.Id, before: null);
        Assert.False(page.CanSend);
        Assert.Single(page.Messages);
    }

    [Fact]
    public async Task PastItsWindow_ChatIsClosed_BeforeTheWorkerEndsIt()
    {
        var (_, guest, gathering) = await GatheringWithGuestAsync();
        var stored = await _db.Gatherings.FindAsync(gathering.Id);
        stored!.StartsAt = DateTime.UtcNow.AddHours(-3);
        stored.EndsAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SendMessageAsync(gathering.Id, guest.Id, Say("Still here?")));
    }

    [Fact]
    public async Task Messages_ArePagedOldestFirst()
    {
        var (host, _, gathering) = await GatheringWithGuestAsync();
        var start = DateTime.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 5; i++)
        {
            _db.GatheringMessages.Add(new GatheringMessage
            {
                Id = Guid.NewGuid(),
                GatheringId = gathering.Id,
                SenderId = host.Id,
                Body = $"m{i}",
                CreatedAt = start.AddMinutes(i)
            });
        }
        await _db.SaveChangesAsync();

        var latest = await _service.GetMessagesAsync(gathering.Id, host.Id, before: null, limit: 3);
        Assert.Equal(new[] { "m2", "m3", "m4" }, latest.Messages.Select(m => m.Body));
        Assert.True(latest.HasMore);

        var earlier = await _service.GetMessagesAsync(
            gathering.Id, host.Id, before: latest.Messages[0].CreatedAt, limit: 3);
        Assert.Equal(new[] { "m0", "m1" }, earlier.Messages.Select(m => m.Body));
        Assert.False(earlier.HasMore);
    }

    // ===== CHAT RECEIPTS =====

    private static UpdateChatReceiptRequest Receipt(DateTime? delivered = null, DateTime? read = null) =>
        new() { DeliveredUpTo = delivered, ReadUpTo = read };

    [Fact]
    public async Task Sending_MarksYourOwnMessageRead_ButNotOthers()
    {
        var (host, guest, gathering) = await GatheringWithGuestAsync();

        var sent = await _service.SendMessageAsync(gathering.Id, guest.Id, Say("hi"));

        Assert.Equal(sent.CreatedAt, (await _service.GetAsync(gathering.Id, guest.Id)).MyChatReadUpTo);
        Assert.Null((await _service.GetAsync(gathering.Id, host.Id)).MyChatReadUpTo);
    }

    [Fact]
    public async Task Receipts_MoveForward_ReadImpliesDelivered_AndOthersAreTold()
    {
        var (host, guest, gathering) = await GatheringWithGuestAsync();
        var first = await _service.SendMessageAsync(gathering.Id, guest.Id, Say("one"));
        var second = await _service.SendMessageAsync(gathering.Id, guest.Id, Say("two"));
        _notifier.Sent.Clear();

        await _service.UpdateChatReceiptAsync(gathering.Id, host.Id, Receipt(read: second.CreatedAt));
        Assert.Equal(new[] { (guest.Id, $"GatheringChatReceipt:{host.Id}") }, _notifier.Sent);

        // An older receipt arriving late changes nothing and tells no one
        _notifier.Sent.Clear();
        await _service.UpdateChatReceiptAsync(gathering.Id, host.Id, Receipt(delivered: first.CreatedAt, read: first.CreatedAt));
        Assert.Empty(_notifier.Sent);

        var receipts = (await _service.GetMessagesAsync(gathering.Id, guest.Id, before: null)).Receipts;
        var hosts = receipts.Single(r => r.Member.Id == host.Id);
        Assert.Equal(second.CreatedAt, hosts.ReadUpTo);
        Assert.Equal(second.CreatedAt, hosts.DeliveredUpTo);
        Assert.Equal(2, receipts.Count);
    }

    [Fact]
    public async Task Receipts_CannotRunAheadOfTheClock()
    {
        var (host, _, gathering) = await GatheringWithGuestAsync();

        await _service.UpdateChatReceiptAsync(gathering.Id, host.Id, Receipt(read: DateTime.UtcNow.AddDays(1)));

        Assert.True((await _service.GetAsync(gathering.Id, host.Id)).MyChatReadUpTo <= DateTime.UtcNow);
    }

    [Fact]
    public async Task NonMembers_CannotSendReceipts()
    {
        var (_, _, gathering) = await GatheringWithGuestAsync();
        var outsider = await AddUserAsync("outsider");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.UpdateChatReceiptAsync(gathering.Id, outsider.Id, Receipt(read: DateTime.UtcNow)));
    }
}
