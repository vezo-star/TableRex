using System.Text.Encodings.Web;
using System.Text.Json;
using BookingPlatform.Application.Common.Idempotency;
using Microsoft.Extensions.Options;

namespace BookingPlatform.Api.Configuration;

/// <summary>Serialises with the MVC JSON options, so stored idempotent responses match what the API writes.</summary>
public sealed class ApiResponseSerializer(IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> options) : IResponseSerializer
{
    /// <summary>MVC's output formatter falls back to the relaxed encoder when none is configured; mirror that.</summary>
    private readonly JsonSerializerOptions _options = options.Value.JsonSerializerOptions.Encoder is null
        ? new JsonSerializerOptions(options.Value.JsonSerializerOptions) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
        : options.Value.JsonSerializerOptions;

    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, _options);
}
