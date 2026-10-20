using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChessApp.Backend.Services;

/// <summary>
/// Writes every DateTime as UTC with a trailing "Z". Times are stored as UTC in
/// "timestamp without time zone" columns, so EF hands them back with Kind
/// Unspecified; serialized as-is they lack a zone and clients read them as
/// local time, shifting them by the device's UTC offset.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };
        writer.WriteStringValue(utc);
    }
}
