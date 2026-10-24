using ChessApp.Backend.Services;
using Npgsql;
using Xunit;

namespace ChessApp.Backend.Tests;

public class PostgresUrlTests
{
    [Fact]
    public void NeonStyleUrl_BecomesAnNpgsqlConnectionString()
    {
        var parsed = new NpgsqlConnectionStringBuilder(PostgresUrl.ToConnectionString(
            "postgresql://neondb_owner:p%40ss:word@ep-cool-1.eu-central-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require"));

        Assert.Equal("ep-cool-1.eu-central-1.aws.neon.tech", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("neondb", parsed.Database);
        Assert.Equal("neondb_owner", parsed.Username);
        Assert.Equal("p@ss:word", parsed.Password);
        Assert.Equal(SslMode.Require, parsed.SslMode);
    }

    [Fact]
    public void PortAndSslModeFromTheUrl_AreKept()
    {
        var parsed = new NpgsqlConnectionStringBuilder(PostgresUrl.ToConnectionString(
            "postgres://app:secret@db.internal:6543/chess?sslmode=disable"));

        Assert.Equal(6543, parsed.Port);
        Assert.Equal(SslMode.Disable, parsed.SslMode);
    }
}
