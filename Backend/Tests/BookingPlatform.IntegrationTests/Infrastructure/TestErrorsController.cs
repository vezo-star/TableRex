using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using BookingPlatform.Application.Common.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace BookingPlatform.IntegrationTests.Infrastructure;

/// <summary>
/// Exists only in the test assembly to exercise the API's error pipeline. Never part of the Api.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("__tests/errors")]
public sealed class TestErrorsController : ControllerBase
{
    public const string SensitiveDetail = "Host=db.internal;Password=hunter2";

    [HttpGet("unhandled")]
    public IActionResult Unhandled() => throw new InvalidOperationException(SensitiveDetail);

    [HttpGet("database-unreachable")]
    public IActionResult DatabaseUnreachable() =>
        throw new NpgsqlException($"Failed to connect to {SensitiveDetail}", new SocketException((int)SocketError.ConnectionRefused));

    [HttpGet("not-found")]
    public IActionResult NotFoundError() => throw new NotFoundAppException("VENUE_NOT_FOUND", "Venue was not found.");

    [HttpGet("conflict")]
    public IActionResult ConflictError() => throw new ConflictAppException("RESOURCE_NOT_AVAILABLE", "Resource is not available.");

    [HttpGet("forbidden")]
    public IActionResult ForbiddenError() => throw new ForbiddenAppException();

    [HttpGet("validation")]
    public IActionResult ValidationError() =>
        throw new ValidationAppException(new Dictionary<string, string[]> { ["partySize"] = ["Party size must be greater than 0."] });

    [HttpPost("model")]
    public IActionResult ModelValidation(TestModel model) => Ok(model);

    public sealed record TestModel([Range(1, 100)] int PartySize);
}
