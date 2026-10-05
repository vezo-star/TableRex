using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookingPlatform.Api.Configuration;

/// <summary>Venue-local times travel as <c>HH:mm</c> (API.md §1); <c>HH:mm:ss</c> is accepted on input.</summary>
public sealed class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = ["HH:mm", "HH:mm:ss"];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value is not null && TimeOnly.TryParseExact(value, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        throw new JsonException("Expected a time in HH:mm format.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(value.Second == 0 ? "HH:mm" : "HH:mm:ss", CultureInfo.InvariantCulture));
}
