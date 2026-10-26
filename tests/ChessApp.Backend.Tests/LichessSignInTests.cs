using ChessApp.Backend.Data;
using ChessApp.Backend.Models;
using ChessApp.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessApp.Backend.Tests;

public class LichessSignInTests
{
    private readonly ApplicationDbContext _db;
    private readonly FakeLichessClient _lichess = new();
    private readonly LichessIntegrationService _service;

    public LichessSignInTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new LichessIntegrationService(
            _db, _lichess, new MemoryCache(new MemoryCacheOptions()), NullLogger<LichessIntegrationService>.Instance);
    }

    private Task<User> SignInAsync()
    {
        var start = _service.StartSignIn();
        return _service.CompleteSignInAsync("code", start.State);
    }

    [Fact]
    public async Task NewLichessPlayer_GetsAnAccountWithoutPassword()
    {
        _lichess.User = new LichessUser { Id = "magnus-fan", Username = "Magnus-Fan" };
        _lichess.Email = "Fan@Example.com";

        var user = await SignInAsync();

        Assert.Equal("Magnus_Fan", user.Username);
        Assert.Equal("fan@example.com", user.Email);
        Assert.Equal("", user.PasswordHash);
        Assert.Equal("Magnus-Fan", user.LichessUsername);
        Assert.True((await _db.LinkedAccounts.SingleAsync()).IsVerified);
        Assert.Contains("scope=email", _lichess.LastAuthorizeUrl);
        Assert.True(_lichess.Revoked);
    }

    [Fact]
    public async Task ReturningPlayer_GetsTheSameAccount()
    {
        _lichess.User = new LichessUser { Id = "kasparov", Username = "Kasparov" };
        var first = await SignInAsync();
        var second = await SignInAsync();

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task PlayerWhoLinkedLichess_SignsIntoTheirExistingAccount()
    {
        var existing = new User { Id = Guid.NewGuid(), Email = "a@example.com", Username = "alice", PasswordHash = "x" };
        _db.Users.Add(existing);
        await _db.SaveChangesAsync();
        _lichess.User = new LichessUser { Id = "alice-l", Username = "Alice-L" };
        var link = _service.StartAuthorization(existing.Id);
        await _service.CompleteAuthorizationAsync(existing.Id, "code", link.State);

        var user = await SignInAsync();

        Assert.Equal(existing.Id, user.Id);
    }

    [Fact]
    public async Task EmailOfAnotherAccount_IsNotTakenOver()
    {
        _db.Users.Add(new User { Id = Guid.NewGuid(), Email = "a@example.com", Username = "alice", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        _lichess.User = new LichessUser { Id = "someone", Username = "Someone" };
        _lichess.Email = "a@example.com";

        await Assert.ThrowsAsync<InvalidOperationException>(SignInAsync);
    }

    [Fact]
    public async Task TakenUsername_GetsANumber()
    {
        _db.Users.Add(new User { Id = Guid.NewGuid(), Email = "b@example.com", Username = "bob", PasswordHash = "x" });
        await _db.SaveChangesAsync();
        _lichess.User = new LichessUser { Id = "bob", Username = "bob" };

        var user = await SignInAsync();

        Assert.Equal("bob2", user.Username);
        Assert.Equal("bob@lichess.invalid", user.Email);
    }

    [Fact]
    public async Task LinkingState_CannotBeUsedToSignIn()
    {
        _lichess.User = new LichessUser { Id = "x", Username = "xyz" };
        var link = _service.StartAuthorization(Guid.NewGuid());

        await Assert.ThrowsAsync<LichessAuthException>(() => _service.CompleteSignInAsync("code", link.State));
    }

    [Fact]
    public async Task LichessOnlyPlayer_CannotDisconnectLichess()
    {
        _lichess.User = new LichessUser { Id = "solo", Username = "Solo" };
        var user = await SignInAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UnlinkAsync(user.Id));
    }

    private class FakeLichessClient : ILichessClient
    {
        public LichessUser User { get; set; } = null!;
        public string? Email { get; set; }
        public string LastAuthorizeUrl { get; private set; } = "";
        public bool Revoked { get; private set; }

        public string BuildAuthorizeUrl(string state, string codeChallenge, string? scope = null) =>
            LastAuthorizeUrl = $"https://lichess.test/oauth?state={state}&scope={scope}";
        public Task<string> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default) =>
            Task.FromResult("token");
        public Task<LichessUser> GetAccountAsync(string accessToken, CancellationToken ct = default) =>
            Task.FromResult(User);
        public Task<string?> GetEmailAsync(string accessToken, CancellationToken ct = default) =>
            Task.FromResult(Email);
        public Task RevokeTokenAsync(string accessToken, CancellationToken ct = default)
        {
            Revoked = true;
            return Task.CompletedTask;
        }
        public Task<LichessUser?> GetUserAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<LichessUser?>(User);
        public Task<List<LichessUser>> GetUsersAsync(IReadOnlyCollection<string> ids, CancellationToken ct = default) =>
            Task.FromResult(new List<LichessUser> { User });
    }
}
