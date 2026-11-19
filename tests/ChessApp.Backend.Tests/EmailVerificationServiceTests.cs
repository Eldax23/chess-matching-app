using System.Text.RegularExpressions;
using ChessApp.Backend.Data;
using ChessApp.Backend.Models;
using ChessApp.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChessApp.Backend.Tests;

public class EmailVerificationServiceTests
{
    private readonly ApplicationDbContext _db;
    private readonly FakeEmailSender _sender = new();
    private readonly EmailVerificationService _service;
    private readonly User _user;

    public EmailVerificationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ApplicationDbContext(options);
        _service = new EmailVerificationService(_db, _sender, NullLogger<EmailVerificationService>.Instance);

        _user = new User { Id = Guid.NewGuid(), Email = "a@example.com", Username = "alice", PasswordHash = "x" };
        _db.Users.Add(_user);
        _db.SaveChanges();
    }

    private string SentCode() => Regex.Match(_sender.LastBody!, @"\b\d{6}\b").Value;

    [Fact]
    public async Task RightCode_VerifiesTheEmail()
    {
        await _service.SendCodeAsync(_user);
        Assert.Equal("a@example.com", _sender.LastTo);

        await _service.VerifyAsync(_user.Id, SentCode());

        Assert.True(_user.IsEmailVerified);
        Assert.Null(_user.EmailVerificationCodeHash);
    }

    [Fact]
    public async Task CodeIsNotStoredInPlainText()
    {
        await _service.SendCodeAsync(_user);

        Assert.DoesNotContain(SentCode(), _user.EmailVerificationCodeHash);
    }

    [Fact]
    public async Task WrongCode_IsRejectedAndCounted()
    {
        await _service.SendCodeAsync(_user);
        var wrong = SentCode() == "000000" ? "111111" : "000000";

        var ex = await Assert.ThrowsAsync<EmailVerificationException>(() => _service.VerifyAsync(_user.Id, wrong));

        Assert.Equal("InvalidCode", ex.Error);
        Assert.Equal(1, _user.EmailVerificationFailedAttempts);
        Assert.False(_user.IsEmailVerified);
    }

    [Fact]
    public async Task AfterFiveWrongTries_EvenTheRightCodeFails()
    {
        await _service.SendCodeAsync(_user);
        var code = SentCode();
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < EmailVerificationService.MaxAttempts; i++)
            await Assert.ThrowsAsync<EmailVerificationException>(() => _service.VerifyAsync(_user.Id, wrong));

        var ex = await Assert.ThrowsAsync<EmailVerificationException>(() => _service.VerifyAsync(_user.Id, code));

        Assert.Equal("TooManyAttempts", ex.Error);
    }

    [Fact]
    public async Task ExpiredCode_IsRejected()
    {
        await _service.SendCodeAsync(_user);
        _user.EmailVerificationCodeExpiresAt = DateTime.UtcNow.AddSeconds(-1);

        var ex = await Assert.ThrowsAsync<EmailVerificationException>(() => _service.VerifyAsync(_user.Id, SentCode()));

        Assert.Equal("CodeExpired", ex.Error);
    }

    [Fact]
    public async Task Resending_WithinAMinute_IsRefused()
    {
        await _service.SendCodeAsync(_user);

        var ex = await Assert.ThrowsAsync<EmailVerificationException>(() => _service.SendCodeAsync(_user));

        Assert.Equal("TooSoon", ex.Error);
        Assert.InRange(ex.RetryAfterSeconds!.Value, 1, 60);
        Assert.Equal(1, _sender.Sent);
    }

    [Fact]
    public async Task NewCode_ReplacesTheOldOne()
    {
        await _service.SendCodeAsync(_user);
        var first = SentCode();
        _user.EmailVerificationCodeSentAt = DateTime.UtcNow.AddMinutes(-2);
        await _service.SendCodeAsync(_user);
        var second = SentCode();

        if (first != second)
            await Assert.ThrowsAsync<EmailVerificationException>(() => _service.VerifyAsync(_user.Id, first));
        await _service.VerifyAsync(_user.Id, second);

        Assert.True(_user.IsEmailVerified);
    }

    [Fact]
    public async Task AbandonedAccounts_AreDeletedAfterAWeek()
    {
        var stale = new User
        {
            Id = Guid.NewGuid(), Email = "old@example.com", Username = "old", PasswordHash = "x",
            EmailVerificationRequestedAt = DateTime.UtcNow.AddDays(-8)
        };
        var verified = new User
        {
            Id = Guid.NewGuid(), Email = "ok@example.com", Username = "ok", PasswordHash = "x",
            EmailVerificationRequestedAt = DateTime.UtcNow.AddDays(-8), EmailVerifiedAt = DateTime.UtcNow.AddDays(-7)
        };
        _db.Users.AddRange(stale, verified);
        await _db.SaveChangesAsync();
        await _service.SendCodeAsync(_user); // recent, so kept

        var deleted = await _service.DeleteAbandonedAccountsAsync(TimeSpan.FromDays(7));

        Assert.Equal(1, deleted);
        Assert.Equal(new[] { "alice", "ok" }, await _db.Users.Select(u => u.Username).OrderBy(n => n).ToArrayAsync());
    }

    [Fact]
    public async Task AccountsThatNeverGotACode_AreNotDeleted()
    {
        _user.CreatedAt = DateTime.UtcNow.AddDays(-30);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await _service.DeleteAbandonedAccountsAsync(TimeSpan.FromDays(7)));
    }

    private class FakeEmailSender : IEmailSender
    {
        public string? LastTo { get; private set; }
        public string? LastBody { get; private set; }
        public int Sent { get; private set; }

        public Task SendAsync(string toAddress, string subject, string textBody, CancellationToken ct = default)
        {
            LastTo = toAddress;
            LastBody = textBody;
            Sent++;
            return Task.CompletedTask;
        }
    }
}
