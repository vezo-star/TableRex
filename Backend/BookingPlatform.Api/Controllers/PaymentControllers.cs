using BookingPlatform.Api.Security;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Payments;
using BookingPlatform.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingPlatform.Api.Controllers;

/// <summary>
/// Inbound provider webhooks. Anonymous at the HTTP level: each provider adapter authenticates the request itself
/// (signature and replay window) before anything is stored, and events are applied once per provider event id.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/payments/webhooks")]
[Produces("application/json")]
public sealed class PaymentWebhooksController(PaymentService payments) : ControllerBase
{
    public const int MaxBodyBytes = 64 * 1024;

    [HttpPost("{provider}")]
    [RateLimit("payment-webhook", 600, 60)]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(string provider, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var applied = await payments.HandleWebhookAsync(provider, new PaymentWebhookRequest(headers, buffer.ToArray()), cancellationToken);
        return Ok(new { received = true, applied });
    }
}

/// <summary>
/// Completes a checkout of the Simulated provider (Development/Test only; the provider is never registered in Production).
/// The request carries the checkout token from the checkout link and is turned into a signed webhook that goes through
/// the real webhook pipeline.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/payments/simulated")]
[Produces("application/json")]
public sealed class SimulatedCheckoutController(PaymentService payments, IPaymentRepository repository, IServiceProvider services) : ControllerBase
{
    public sealed record CompleteRequest(Guid PaymentId, string Token, string Outcome);

    [HttpPost("complete")]
    [RateLimit("payment-simulated", 30, 60)]
    public async Task<IActionResult> Complete(CompleteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var provider = services.GetService<SimulatedPaymentProvider>();
        if (provider is null || !provider.IsValidCheckoutToken(request.PaymentId, request.Token))
        {
            throw new NotFoundAppException(ErrorCodes.PaymentNotFound, "The payment was not found.");
        }

        var type = request.Outcome?.ToUpperInvariant() switch
        {
            "SUCCEEDED" => PaymentEventTypes.PaymentSucceeded,
            "FAILED" => PaymentEventTypes.PaymentFailed,
            "CANCELLED" => PaymentEventTypes.PaymentCancelled,
            _ => throw new ValidationAppException("outcome", "Outcome must be SUCCEEDED, FAILED or CANCELLED."),
        };
        var payment = await repository.FindByIdAsync(request.PaymentId, cancellationToken)
            ?? throw new NotFoundAppException(ErrorCodes.PaymentNotFound, "The payment was not found.");

        var webhook = provider.CreateSignedWebhook(new SimulatedEvent("evt_" + Guid.NewGuid().ToString("N"), type, payment.Id, payment.ProviderReference,
            payment.AmountMinor, payment.Currency, FailureReason: type == PaymentEventTypes.PaymentFailed ? "Simulated card decline." : null));
        await payments.HandleWebhookAsync(SimulatedPaymentProvider.ProviderName, webhook, cancellationToken);
        return NoContent();
    }
}

[ApiController]
[Produces("application/json")]
public sealed class BookingPaymentsController(PaymentService payments) : ControllerBase
{
    [HttpGet("api/v1/bookings/{bookingId:guid}/payments")]
    [HasPermission(Permissions.PaymentView)]
    public Task<IReadOnlyList<PaymentDto>> List(Guid bookingId, CancellationToken cancellationToken) =>
        payments.ListForBookingAsync(bookingId, cancellationToken);

    /// <summary>Refunds all (no amount) or part of a paid payment. Requires <c>Idempotency-Key</c>.</summary>
    [HttpPost("api/v1/bookings/{bookingId:guid}/payments/{paymentId:guid}/refunds")]
    [HasPermission(Permissions.PaymentRefund)]
    [ProducesResponseType<RefundDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Refund(Guid bookingId, Guid paymentId, RefundRequest request, CancellationToken cancellationToken)
    {
        var route = $"/api/v1/bookings/{bookingId}/payments/{paymentId}/refunds";
        var idempotency = this.CreateIdempotencyRequest(route, request);
        var result = await payments.RefundAsync(bookingId, paymentId, request, idempotency, cancellationToken);
        return this.IdempotentCreated(result, _ => $"/api/v1/bookings/{bookingId}/payments");
    }

    [HttpGet("api/v1/venues/{venueId:guid}/payment-settings")]
    [HasPermission(Permissions.VenueView)]
    public Task<VenuePaymentSettingsDto> GetSettings(Guid venueId, CancellationToken cancellationToken) =>
        payments.GetSettingsAsync(venueId, cancellationToken);

    [HttpPut("api/v1/venues/{venueId:guid}/payment-settings")]
    [HasPermission(Permissions.VenueManage)]
    public Task<VenuePaymentSettingsDto> UpdateSettings(Guid venueId, UpdateVenuePaymentSettingsRequest request, CancellationToken cancellationToken) =>
        payments.UpdateSettingsAsync(venueId, request, cancellationToken);
}
