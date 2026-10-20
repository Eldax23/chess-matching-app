using System.Text.Json;
using ChessApp.Backend.Services;
using Xunit;

namespace ChessApp.Backend.Tests;

public class UtcDateTimeConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new UtcDateTimeConverter() } };

    [Fact]
    public void UnspecifiedTimes_FromTheDatabase_AreWrittenAsUtc()
    {
        var fromDb = new DateTime(2026, 10, 20, 13, 25, 4, DateTimeKind.Unspecified);
        Assert.Equal("\"2026-10-20T13:25:04Z\"", JsonSerializer.Serialize(fromDb, Options));
    }

    [Fact]
    public void NullableTimes_AreWrittenAsUtcToo()
    {
        DateTime? fromDb = new DateTime(2026, 10, 20, 13, 25, 4, DateTimeKind.Unspecified);
        Assert.Equal("\"2026-10-20T13:25:04Z\"", JsonSerializer.Serialize(fromDb, Options));
        Assert.Equal("null", JsonSerializer.Serialize<DateTime?>(null, Options));
    }
}
