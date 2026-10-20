using ChessApp.Backend.Data;
using ChessApp.Backend.DTOs;
using ChessApp.Backend.Models;
using ChessApp.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using Xunit;

namespace ChessApp.Backend.Tests;

public class ReviewServiceTests
{
    private static readonly GeometryFactory Geometry = new(new PrecisionModel(), 4326);

    private readonly ApplicationDbContext _db;
    private readonly ReviewService _service;

    public ReviewServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new ReviewService(_db, NullLogger<ReviewService>.Instance);
    }

    private User AddUser(string username)
    {
        var user = new User { Id = Guid.NewGuid(), Email = $"{username}@example.com", Username = username, PasswordHash = "x" };
        _db.Users.Add(user);
        return user;
    }

    private async Task<Gathering> AddGatheringAsync(GatheringStatus status, DateTime endsAt, params User[] members)
    {
        var gathering = new Gathering
        {
            Id = Guid.NewGuid(),
            HostId = members[0].Id,
            VenueName = "Park",
            Location = Geometry.CreatePoint(new Coordinate(31.2, 30.0)),
            StartsAt = endsAt.AddHours(-2),
            EndsAt = endsAt,
            Status = status,
            EndedAt = status == GatheringStatus.Ended ? endsAt : null
        };
        foreach (var member in members)
            gathering.Members.Add(new GatheringMember { GatheringId = gathering.Id, UserId = member.Id });
        _db.Gatherings.Add(gathering);
        await _db.SaveChangesAsync();
        return gathering;
    }

    [Fact]
    public async Task Members_CanReviewEachOther_AfterTheGatheringEnds()
    {
        var a = AddUser("a");
        var b = AddUser("b");
        var c = AddUser("c");
        var gathering = await AddGatheringAsync(GatheringStatus.Ended, DateTime.UtcNow.AddHours(-1), a, b, c);

        await _service.CreateReviewAsync(a.Id, new CreateReviewRequest
        {
            GatheringId = gathering.Id, RevieweeId = b.Id, Tags = new() { ReviewTags.Friendly, ReviewTags.OnTime }
        });
        await _service.CreateReviewAsync(c.Id, new CreateReviewRequest
        {
            GatheringId = gathering.Id, RevieweeId = b.Id, Tags = new() { ReviewTags.Friendly }
        });
        await _service.CreateReviewAsync(a.Id, new CreateReviewRequest
        {
            GatheringId = gathering.Id, RevieweeId = c.Id, NoShow = true
        });

        var forB = await _service.GetReviewsForUserAsync(b.Id);
        Assert.Equal(2, forB.Count);
        Assert.Equal(ReviewTags.Friendly, forB.TagCounts[0].Tag);
        Assert.Equal(2, forB.TagCounts[0].Count);
        Assert.Equal(0, forB.NoShowCount);
        Assert.Equal(1, (await _service.GetReviewsForUserAsync(c.Id)).NoShowCount);
    }

    [Fact]
    public async Task CantReview_BeforeTheGatheringEnds()
    {
        var a = AddUser("a");
        var b = AddUser("b");
        var gathering = await AddGatheringAsync(GatheringStatus.Gathered, DateTime.UtcNow.AddHours(1), a, b);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateReviewAsync(a.Id,
            new CreateReviewRequest { GatheringId = gathering.Id, RevieweeId = b.Id, Tags = new() { ReviewTags.Friendly } }));
    }

    [Fact]
    public async Task CantReview_PeopleOutsideTheGathering_OrTwice()
    {
        var a = AddUser("a");
        var b = AddUser("b");
        var stranger = AddUser("stranger");
        var gathering = await AddGatheringAsync(GatheringStatus.Ended, DateTime.UtcNow.AddHours(-1), a, b);
        var request = new CreateReviewRequest { GatheringId = gathering.Id, RevieweeId = b.Id, Tags = new() { ReviewTags.GoodCompany } };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateReviewAsync(stranger.Id, request));
        await _service.CreateReviewAsync(a.Id, request);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateReviewAsync(a.Id, request));
    }

    [Fact]
    public async Task ReviewsClose_AWeekAfterTheGathering()
    {
        var a = AddUser("a");
        var b = AddUser("b");
        var gathering = await AddGatheringAsync(GatheringStatus.Ended, DateTime.UtcNow.AddDays(-8), a, b);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateReviewAsync(a.Id,
            new CreateReviewRequest { GatheringId = gathering.Id, RevieweeId = b.Id, Tags = new() { ReviewTags.Friendly } }));
    }
}
