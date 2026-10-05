using BookingPlatform.Domain.Bookings;
using FluentValidation;

namespace BookingPlatform.Application.Bookings;

public static class BookingRules
{
    public const int MaxResources = 20;

    public static IRuleBuilderOptions<T, AllocationRequest?> ValidAllocation<T>(this IRuleBuilder<T, AllocationRequest?> rule) =>
        rule.Must(a => a is null || AllocationModes.IsValid(a.Mode))
            .WithMessage("Allocation mode must be AUTO, RESOURCES or COMBINATION.")
            .Must(a => a is null || a.Mode != AllocationModes.Resources
                || (a.ResourceIds is { Count: > 0 and <= MaxResources } ids && ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count))
            .WithMessage($"RESOURCES allocation requires 1-{MaxResources} distinct resource ids.")
            .Must(a => a is null || a.Mode != AllocationModes.Combination || a.CombinationId is { } id && id != Guid.Empty)
            .WithMessage("COMBINATION allocation requires a combinationId.");
}

public sealed class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(r => r.VenueId).NotEmpty();
        RuleFor(r => r.Date).NotEmpty();
        RuleFor(r => r.PartySize).InclusiveBetween(1, 1000);
        RuleFor(r => r.DurationMinutes).InclusiveBetween(5, 1440).When(r => r.DurationMinutes is not null);
        RuleFor(r => r.Allocation).ValidAllocation();
        RuleFor(r => r.Allocation)
            .Must(a => a is null || a.Mode == AllocationModes.Auto)
            .When(r => r.HoldToken is not null)
            .WithMessage("A hold already determines the resources; omit allocation when converting a hold.");
        RuleFor(r => r.HoldToken).MaximumLength(100);
        RuleFor(r => r.Source)
            .Must(s => s is null || BookingSource.StaffSelectable.Contains(s))
            .WithMessage("Source must be STAFF, PHONE or WALK_IN.");
        RuleFor(r => r.Status)
            .Must(s => s is null or BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.Seated)
            .WithMessage("Status must be PENDING, CONFIRMED or SEATED.");
        RuleFor(r => r.SpecialRequests).MaximumLength(2000);
        RuleFor(r => r.InternalNotes).MaximumLength(4000);
        RuleFor(r => r.Customer)
            .NotNull()
            .When(r => r.Source != BookingSource.WalkIn)
            .WithMessage("A customer is required (walk-ins may omit it).");
        RuleFor(r => r.Customer!).SetValidator(new BookingCustomerInputValidator()).When(r => r.Customer is not null);
    }
}

public sealed class BookingCustomerInputValidator : AbstractValidator<BookingCustomerInput>
{
    public BookingCustomerInputValidator()
    {
        RuleFor(c => c.CustomerId).NotEqual(Guid.Empty).When(c => c.CustomerId is not null);
        RuleFor(c => c.FirstName)
            .Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length <= 100)
            .When(c => c.CustomerId is null)
            .WithMessage("First name is required (1-100 characters) for a new customer.");
        RuleFor(c => c.LastName).MaximumLength(100);
        RuleFor(c => c.Email).MaximumLength(254).EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.Email));
        RuleFor(c => c.Phone).MaximumLength(40);
        RuleFor(c => c.Phone)
            .Must(p => BookingService.NormalizePhone(p) is not null)
            .When(c => !string.IsNullOrWhiteSpace(c.Phone))
            .WithMessage("Phone must contain 4-20 digits.");
    }
}

public sealed class UpdateBookingRequestValidator : AbstractValidator<UpdateBookingRequest>
{
    public UpdateBookingRequestValidator()
    {
        RuleFor(r => r.PartySize).InclusiveBetween(1, 1000);
        RuleFor(r => r.SpecialRequests).MaximumLength(2000);
        RuleFor(r => r.InternalNotes).MaximumLength(4000);
        RuleFor(r => r.CustomerId).NotEqual(Guid.Empty).When(r => r.CustomerId is not null);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(1).When(r => r.Version is not null);
    }
}

public sealed class MoveBookingRequestValidator : AbstractValidator<MoveBookingRequest>
{
    public MoveBookingRequestValidator()
    {
        RuleFor(r => r.DurationMinutes).InclusiveBetween(5, 1440).When(r => r.DurationMinutes is not null);
        RuleFor(r => r.Allocation).ValidAllocation();
        RuleFor(r => r.Version).GreaterThanOrEqualTo(1).When(r => r.Version is not null);
        RuleFor(r => r)
            .Must(r => r.Date is not null || r.Time is not null || r.DurationMinutes is not null || r.Allocation is not null)
            .WithName("move")
            .WithMessage("Provide a new date, time, duration or allocation.");
    }
}

public sealed class CancelBookingRequestValidator : AbstractValidator<CancelBookingRequest>
{
    public CancelBookingRequestValidator()
    {
        RuleFor(r => r.Reason).MaximumLength(500);
        RuleFor(r => r.Version).GreaterThanOrEqualTo(1).When(r => r.Version is not null);
    }
}

public sealed class BookingListQueryValidator : AbstractValidator<BookingListQuery>
{
    public BookingListQueryValidator()
    {
        RuleFor(q => q.Page.Page).GreaterThanOrEqualTo(1).OverridePropertyName("page");
        RuleFor(q => q.Page.PageSize).InclusiveBetween(1, Common.Pagination.PageRequest.MaxPageSize).OverridePropertyName("pageSize");
        RuleFor(q => q.Statuses)
            .Must(s => s is null || s.All(BookingStatus.IsValid))
            .WithName("status")
            .WithMessage("Unknown booking status.");
        RuleFor(q => q.Source).Must(s => s is null || BookingSource.IsValid(s)).WithMessage("Unknown booking source.");
        RuleFor(q => q.Q).MaximumLength(100);
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.From <= q.To)
            .WithName("to")
            .WithMessage("'to' must be on or after 'from'.");
        RuleFor(q => q)
            .Must(q => q.From is null || q.To is null || q.To.Value.DayNumber - q.From.Value.DayNumber <= 366)
            .WithName("to")
            .WithMessage("The date range may span at most 366 days.");
    }
}

public sealed class CreateHoldRequestValidator : AbstractValidator<CreateHoldRequest>
{
    public CreateHoldRequestValidator()
    {
        RuleFor(r => r.VenueId).NotEmpty();
        RuleFor(r => r.Date).NotEmpty();
        RuleFor(r => r.PartySize).InclusiveBetween(1, 1000);
        RuleFor(r => r.DurationMinutes).InclusiveBetween(5, 1440).When(r => r.DurationMinutes is not null);
        RuleFor(r => r.Allocation).ValidAllocation();
    }
}

public sealed class CreateBlockRequestValidator : AbstractValidator<CreateBlockRequest>
{
    public CreateBlockRequestValidator()
    {
        RuleFor(r => r.VenueId).NotEmpty();
        RuleFor(r => r.ResourceIds)
            .NotNull()
            .Must(ids => ids is { Count: > 0 and <= 50 } && ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Count)
            .WithMessage("Provide 1-50 distinct resource ids.");
        RuleFor(r => r.Reason).Must(BlockReasons.IsValid).WithMessage($"Reason must be one of: {string.Join(", ", BlockReasons.All)}.");
        RuleFor(r => r.Notes).MaximumLength(1000);
        RuleFor(r => r.EndsAt).GreaterThan(r => r.StartsAt).WithMessage("The block must end after it starts.");
        RuleFor(r => r)
            .Must(r => r.EndsAt - r.StartsAt <= TimeSpan.FromDays(31))
            .WithName("endsAt")
            .WithMessage("A block may last at most 31 days.");
    }
}
