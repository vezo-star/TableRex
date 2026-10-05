using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Domain.Time;
using BookingPlatform.Domain.Venues;
using FluentValidation;

namespace BookingPlatform.Application.Venues;

internal static class VenueRules
{
    public const int NameMaxLength = 100;
    public const int TextMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MaxAttributes = 20;

    public static readonly int[] SlotIntervals = [5, 10, 15, 20, 30, 60];

    public static IRuleBuilderOptions<T, string> ValidName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage("Enter a name.").MaximumLength(NameMaxLength);

    public static IRuleBuilderOptions<T, string> ValidSlug<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(63).Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("Slug may contain lowercase letters, digits and single hyphens.");

    public static IRuleBuilderOptions<T, string> ValidTimeZone<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(TimeZones.IsValid).WithMessage("Enter a valid IANA time zone, for example Africa/Johannesburg.");

    public static IRuleBuilderOptions<T, string?> OptionalText<T>(this IRuleBuilder<T, string?> rule, int maxLength = TextMaxLength) =>
        rule.MaximumLength(maxLength);

    public static IRuleBuilderOptions<T, IReadOnlyDictionary<string, string>?> ValidAttributes<T>(
        this IRuleBuilder<T, IReadOnlyDictionary<string, string>?> rule) =>
        rule.Must(a => a is null || (a.Count <= MaxAttributes
                                     && a.All(kv => kv.Key.Length is > 0 and <= 40 && kv.Value is { Length: <= 200 })))
            .WithMessage($"Up to {MaxAttributes} attributes; keys 1-40 characters, values up to 200 characters.");
}

public sealed class VenueBookingRulesValidator : AbstractValidator<VenueBookingRules>
{
    public VenueBookingRulesValidator()
    {
        RuleFor(r => r.SlotIntervalMinutes).Must(v => VenueRules.SlotIntervals.Contains(v))
            .WithMessage("Slot interval must be 5, 10, 15, 20, 30 or 60 minutes.");
        RuleFor(r => r.MinDurationMinutes).InclusiveBetween(5, 1440);
        RuleFor(r => r.MaxDurationMinutes).InclusiveBetween(5, 1440).GreaterThanOrEqualTo(r => r.MinDurationMinutes)
            .WithMessage("Maximum duration must be at least the minimum duration.");
        RuleFor(r => r.DefaultDurationMinutes).InclusiveBetween(5, 1440)
            .Must((r, v) => v >= r.MinDurationMinutes && v <= r.MaxDurationMinutes)
            .WithMessage("Default duration must be between the minimum and maximum duration.");
        RuleFor(r => r.TurnoverBufferMinutes).InclusiveBetween(0, 240);
        RuleFor(r => r.MinPartySize).InclusiveBetween(1, 1000);
        RuleFor(r => r.MaxPartySize).InclusiveBetween(1, 1000).GreaterThanOrEqualTo(r => r.MinPartySize)
            .WithMessage("Maximum party size must be at least the minimum party size.");
        RuleFor(r => r.MinLeadTimeMinutes).InclusiveBetween(0, 43200);
        RuleFor(r => r.MaxAdvanceDays).InclusiveBetween(1, 730);
        RuleFor(r => r.HoldTtlSeconds).InclusiveBetween(60, 3600);
        RuleFor(r => r.PublicCancellationCutoffMinutes).InclusiveBetween(0, 10080);
    }
}

public sealed class CreateVenueRequestValidator : AbstractValidator<CreateVenueRequest>
{
    public CreateVenueRequestValidator()
    {
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.Slug).ValidSlug();
        RuleFor(r => r.TimeZone).ValidTimeZone();
        RuleFor(r => r.Description).OptionalText(VenueRules.DescriptionMaxLength);
        RuleFor(r => r.AddressLine1).OptionalText();
        RuleFor(r => r.AddressLine2).OptionalText();
        RuleFor(r => r.City).OptionalText(100);
        RuleFor(r => r.Region).OptionalText(100);
        RuleFor(r => r.PostalCode).OptionalText(20);
        RuleFor(r => r.CountryCode).Matches("^[A-Z]{2}$").When(r => !string.IsNullOrEmpty(r.CountryCode))
            .WithMessage("Use a two-letter ISO country code, for example ZA.");
        RuleFor(r => r.Phone).OptionalText(40);
        RuleFor(r => r.Email).EmailAddress().MaximumLength(254).When(r => !string.IsNullOrEmpty(r.Email));
        RuleFor(r => r.Rules!).SetValidator(new VenueBookingRulesValidator()).When(r => r.Rules is not null);
    }
}

public sealed class UpdateVenueRequestValidator : AbstractValidator<UpdateVenueRequest>
{
    public UpdateVenueRequestValidator()
    {
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.TimeZone).ValidTimeZone();
        RuleFor(r => r.Status).Must(VenueStatus.IsValid).WithMessage("Status must be ACTIVE or INACTIVE.");
        RuleFor(r => r.Description).OptionalText(VenueRules.DescriptionMaxLength);
        RuleFor(r => r.AddressLine1).OptionalText();
        RuleFor(r => r.AddressLine2).OptionalText();
        RuleFor(r => r.City).OptionalText(100);
        RuleFor(r => r.Region).OptionalText(100);
        RuleFor(r => r.PostalCode).OptionalText(20);
        RuleFor(r => r.CountryCode).Matches("^[A-Z]{2}$").When(r => !string.IsNullOrEmpty(r.CountryCode))
            .WithMessage("Use a two-letter ISO country code, for example ZA.");
        RuleFor(r => r.Phone).OptionalText(40);
        RuleFor(r => r.Email).EmailAddress().MaximumLength(254).When(r => !string.IsNullOrEmpty(r.Email));
        RuleFor(r => r.Rules).NotNull().SetValidator(new VenueBookingRulesValidator());
    }
}

public sealed class SectionRequestValidator : AbstractValidator<SectionRequest>
{
    public SectionRequestValidator()
    {
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.Description).OptionalText(500);
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 10000);
    }
}

public sealed class ReplaceOperatingHoursRequestValidator : AbstractValidator<ReplaceOperatingHoursRequest>
{
    public ReplaceOperatingHoursRequestValidator()
    {
        RuleFor(r => r.Periods).NotNull().Must(p => p.Count <= 42).WithMessage("At most 42 periods (6 per day).");
        RuleForEach(r => r.Periods).ChildRules(period =>
        {
            period.RuleFor(p => p.DayOfWeek).InclusiveBetween(1, 7).WithMessage("Day of week must be 1 (Monday) to 7 (Sunday).");
            period.RuleFor(p => p.CloseTime).NotEqual(p => p.OpenTime).WithMessage("Opening and closing times must differ.");
            period.RuleFor(p => p.OpenTime).Must(t => t.Second == 0 && t.Millisecond == 0).WithMessage("Use whole minutes.");
            period.RuleFor(p => p.CloseTime).Must(t => t.Second == 0 && t.Millisecond == 0).WithMessage("Use whole minutes.");
        });
    }
}

public sealed class SpecialHoursRequestValidator : AbstractValidator<SpecialHoursRequest>
{
    public SpecialHoursRequestValidator()
    {
        RuleFor(r => r.Name).OptionalText(100);
        RuleFor(r => r.OpenTime).Null().When(r => r.IsClosed).WithMessage("A closed day has no opening time.");
        RuleFor(r => r.CloseTime).Null().When(r => r.IsClosed).WithMessage("A closed day has no closing time.");
        RuleFor(r => r.OpenTime).NotNull().When(r => !r.IsClosed).WithMessage("Enter an opening time.");
        RuleFor(r => r.CloseTime).NotNull().When(r => !r.IsClosed).WithMessage("Enter a closing time.")
            .NotEqual(r => r.OpenTime).When(r => !r.IsClosed).WithMessage("Opening and closing times must differ.");
    }
}

public sealed class CreateResourceTypeRequestValidator : AbstractValidator<CreateResourceTypeRequest>
{
    public CreateResourceTypeRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().Matches("^[A-Z][A-Z0-9_]{0,39}$")
            .WithMessage("Code must start with a letter and contain only A-Z, 0-9 and underscores (max 40).");
        RuleFor(r => r.Name).ValidName();
    }
}

public sealed class UpdateResourceTypeRequestValidator : AbstractValidator<UpdateResourceTypeRequest>
{
    public UpdateResourceTypeRequestValidator() => RuleFor(r => r.Name).ValidName();
}

public sealed class CreateResourceRequestValidator : AbstractValidator<CreateResourceRequest>
{
    public CreateResourceRequestValidator()
    {
        RuleFor(r => r.VenueId).NotEmpty();
        RuleFor(r => r.ResourceTypeId).NotEmpty();
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.Code).NotEmpty().Must(c => !string.IsNullOrWhiteSpace(c)).MaximumLength(40);
        RuleFor(r => r.MinCapacity).InclusiveBetween(1, 1000);
        RuleFor(r => r.Capacity).InclusiveBetween(1, 1000).GreaterThanOrEqualTo(r => r.MinCapacity)
            .WithMessage("Capacity must be at least the minimum capacity.");
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 10000);
        RuleFor(r => r.Attributes).ValidAttributes();
    }
}

public sealed class UpdateResourceRequestValidator : AbstractValidator<UpdateResourceRequest>
{
    public UpdateResourceRequestValidator()
    {
        RuleFor(r => r.ResourceTypeId).NotEmpty();
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.Code).NotEmpty().Must(c => !string.IsNullOrWhiteSpace(c)).MaximumLength(40);
        RuleFor(r => r.MinCapacity).InclusiveBetween(1, 1000);
        RuleFor(r => r.Capacity).InclusiveBetween(1, 1000).GreaterThanOrEqualTo(r => r.MinCapacity)
            .WithMessage("Capacity must be at least the minimum capacity.");
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 10000);
        RuleFor(r => r.Attributes).ValidAttributes();
    }
}

public sealed class ResourceQueryValidator : AbstractValidator<ResourceQuery>
{
    public ResourceQueryValidator() => RuleFor(q => q.Page).NotNull().SetValidator(new PageRequestValidator());
}

public sealed class CreateFloorPlanRequestValidator : AbstractValidator<CreateFloorPlanRequest>
{
    public CreateFloorPlanRequestValidator()
    {
        RuleFor(r => r.VenueId).NotEmpty();
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.Width).InclusiveBetween(200, 10000);
        RuleFor(r => r.Height).InclusiveBetween(200, 10000);
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 10000);
    }
}

public sealed class UpdateFloorPlanRequestValidator : AbstractValidator<UpdateFloorPlanRequest>
{
    public UpdateFloorPlanRequestValidator()
    {
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.Width).InclusiveBetween(200, 10000);
        RuleFor(r => r.Height).InclusiveBetween(200, 10000);
        RuleFor(r => r.SortOrder).InclusiveBetween(0, 10000);
    }
}

public sealed class ReplaceFloorPlanPositionsRequestValidator : AbstractValidator<ReplaceFloorPlanPositionsRequest>
{
    public ReplaceFloorPlanPositionsRequestValidator()
    {
        RuleFor(r => r.Positions).NotNull().Must(p => p.Count <= 500).WithMessage("At most 500 positions per floor plan.")
            .Must(p => p.Select(x => x.ResourceId).Distinct().Count() == p.Count).WithMessage("Each resource may appear only once.");
        RuleForEach(r => r.Positions).ChildRules(position =>
        {
            position.RuleFor(p => p.ResourceId).NotEmpty();
            position.RuleFor(p => p.X).InclusiveBetween(0, 10000);
            position.RuleFor(p => p.Y).InclusiveBetween(0, 10000);
            position.RuleFor(p => p.Width).GreaterThan(0).LessThanOrEqualTo(10000);
            position.RuleFor(p => p.Height).GreaterThan(0).LessThanOrEqualTo(10000);
            position.RuleFor(p => p.Rotation).GreaterThanOrEqualTo(0).LessThan(360);
            position.RuleFor(p => p.Shape).Must(FloorPlanShapes.IsValid).WithMessage("Shape must be RECT or ROUND.");
        });
    }
}

public sealed class UpdateStaffVenuesRequestValidator : AbstractValidator<UpdateStaffVenuesRequest>
{
    public UpdateStaffVenuesRequestValidator()
    {
        RuleFor(r => r.VenueIds).NotNull().Must(v => v.Count <= 500).WithMessage("Too many venues.");
        RuleForEach(r => r.VenueIds).NotEmpty();
    }
}
