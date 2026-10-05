using BookingPlatform.Application.Bookings;
using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Domain.Bookings;
using Microsoft.Extensions.Logging;
using IClock = BookingPlatform.Application.Common.Time.IClock;

namespace BookingPlatform.Application.Payments;

/// <summary>
/// Cancels HELD bookings whose deposit was not paid in time (Worker). Each booking expires in its own transaction under
/// the booking lock, so a payment webhook arriving at the same moment either confirms first or finds the booking expired
/// (and the payment is flagged for a refund).
/// </summary>
public sealed class DepositExpiryService(IPaymentRepository payments, BookingTransitions transitions, IClock clock, ILogger<DepositExpiryService> logger)
{
    public const string Reason = "Deposit not paid in time.";

    public async Task<int> ExpireDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        var due = await payments.ListDueHeldBookingsAsync(clock.UtcNow, batchSize, cancellationToken);
        var expired = 0;
        foreach (var booking in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await transitions.ApplyAsync(booking.TenantId, booking.BookingId, BookingCommand.Expire,
                    new TransitionContext(HistorySources.System, null, Reason)
                    {
                        Precondition = locked =>
                        {
                            if (locked.ExpiresAt is not { } expiresAt || expiresAt > clock.UtcNow)
                            {
                                throw new ConflictAppException(ErrorCodes.InvalidStatusTransition, "The booking is not due to expire.");
                            }
                        },
                    },
                    cancellationToken);
                expired++;
            }
            catch (AppException ex)
            {
                // Confirmed, cancelled or extended meanwhile: nothing to expire.
                logger.LogDebug("Skipped expiring booking {BookingId}: {Code}", booking.BookingId, ex.Code);
            }
        }

        return expired;
    }
}
