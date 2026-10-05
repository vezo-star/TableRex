namespace BookingPlatform.Application.Common.Errors;

/// <summary>
/// Stable application error codes returned in ProblemDetails <c>code</c>. Clients depend on these values; never rename one.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationError = "VALIDATION_ERROR";
    public const string BadRequest = "BAD_REQUEST";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string Conflict = "CONFLICT";
    public const string RateLimited = "RATE_LIMITED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string BusinessRuleViolation = "BUSINESS_RULE_VIOLATION";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

    // Authentication and tenancy
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
    public const string TenantSelectionRequired = "TENANT_SELECTION_REQUIRED";
    public const string PasswordTooWeak = "PASSWORD_TOO_WEAK";
    public const string EmailTaken = "EMAIL_TAKEN";
    public const string SlugTaken = "SLUG_TAKEN";
    public const string LastOwner = "LAST_OWNER";
    public const string TenantNotFound = "TENANT_NOT_FOUND";
    public const string StaffNotFound = "STAFF_NOT_FOUND";
    public const string RoleNotFound = "ROLE_NOT_FOUND";

    // Venues and resources
    public const string VenueNotFound = "VENUE_NOT_FOUND";
    public const string SectionNotFound = "SECTION_NOT_FOUND";
    public const string ResourceTypeNotFound = "RESOURCE_TYPE_NOT_FOUND";
    public const string ResourceNotFound = "RESOURCE_NOT_FOUND";
    public const string CombinationNotFound = "COMBINATION_NOT_FOUND";
    public const string FloorPlanNotFound = "FLOOR_PLAN_NOT_FOUND";
    public const string SpecialHoursNotFound = "SPECIAL_HOURS_NOT_FOUND";
    public const string CodeTaken = "CODE_TAKEN";
    public const string InvalidTimeZone = "INVALID_TIME_ZONE";

    // Bookings
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";
    public const string InvalidBookingTime = "INVALID_BOOKING_TIME";
    public const string PartySizeNotSupported = "PARTY_SIZE_NOT_SUPPORTED";
    public const string OutsideOperatingHours = "OUTSIDE_OPERATING_HOURS";
    public const string BookingNotFound = "BOOKING_NOT_FOUND";
    public const string CustomerNotFound = "CUSTOMER_NOT_FOUND";
    public const string CustomerAnonymized = "CUSTOMER_ANONYMIZED";
    public const string HoldNotFound = "HOLD_NOT_FOUND";
    public const string BlockNotFound = "BLOCK_NOT_FOUND";
    public const string ResourceNotAvailable = "RESOURCE_NOT_AVAILABLE";
    public const string BookingAlreadyCancelled = "BOOKING_ALREADY_CANCELLED";
    public const string InvalidStatusTransition = "INVALID_STATUS_TRANSITION";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";
    public const string HoldExpired = "HOLD_EXPIRED";
    public const string CancellationNotAllowed = "CANCELLATION_NOT_ALLOWED";

    // SaaS and payments
    public const string PlanLimitReached = "PLAN_LIMIT_REACHED";
    public const string PaymentNotFound = "PAYMENT_NOT_FOUND";
    public const string InvalidWebhookSignature = "INVALID_WEBHOOK_SIGNATURE";
}
