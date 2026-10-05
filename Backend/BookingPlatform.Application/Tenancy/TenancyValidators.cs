using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Security;
using BookingPlatform.Application.Identity;
using BookingPlatform.Domain.Identity;
using BookingPlatform.Domain.Time;
using FluentValidation;

namespace BookingPlatform.Application.Tenancy;

internal static class TenancyRules
{
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 254;

    public static IRuleBuilderOptions<T, string> ValidEmail<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(EmailMaxLength).EmailAddress().WithMessage("Enter a valid email address.");

    public static IRuleBuilderOptions<T, string> ValidTimeZone<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(TimeZones.IsValid).WithMessage("Enter a valid IANA time zone, for example Africa/Johannesburg.");

    public static IRuleBuilderOptions<T, string> ValidName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(value => !string.IsNullOrWhiteSpace(value)).MaximumLength(NameMaxLength);
}

public sealed class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator()
    {
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.DefaultTimeZone).ValidTimeZone();
    }
}

public sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    /// <summary>Literal segments of public routes that would shadow <c>GET /api/v1/public/{tenantSlug}</c>.</summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal) { "booking" };

    public CreateTenantRequestValidator()
    {
        RuleFor(r => r.Name).ValidName();
        RuleFor(r => r.Slug).NotEmpty().MaximumLength(63).Matches("^[a-z0-9]+(-[a-z0-9]+)*$")
            .WithMessage("Slug may contain lowercase letters, digits and single hyphens.");
        RuleFor(r => r.Slug).Must(slug => !ReservedSlugs.Contains(slug)).WithMessage("That slug is reserved.");
        RuleFor(r => r.PlanCode).MaximumLength(40).Matches("^[A-Z][A-Z0-9_]{1,39}$").WithMessage("Unknown plan.");
        RuleFor(r => r.DefaultTimeZone).ValidTimeZone();
        RuleFor(r => r.Owner).NotNull();
        RuleFor(r => r.Owner.Email).ValidEmail().When(r => r.Owner is not null);
        RuleFor(r => r.Owner.DisplayName).ValidName().When(r => r.Owner is not null);
        RuleFor(r => r.Owner.Password)
            .Must((request, password) => password is null || PasswordPolicy.Validate(password, request.Owner.Email) is null)
            .WithMessage((request, password) => PasswordPolicy.Validate(password, request.Owner.Email) ?? string.Empty)
            .When(r => r.Owner is not null);
    }
}

public sealed class CreateStaffRequestValidator : AbstractValidator<CreateStaffRequest>
{
    public CreateStaffRequestValidator()
    {
        RuleFor(r => r.Email).ValidEmail();
        RuleFor(r => r.DisplayName).ValidName();
        RuleFor(r => r.Password)
            .Must((request, password) => password is null || PasswordPolicy.Validate(password, request.Email) is null)
            .WithMessage((request, password) => PasswordPolicy.Validate(password, request.Email) ?? string.Empty);
        RuleFor(r => r.Roles).NotNull().NotEmpty().WithMessage("Select at least one role.");
        RuleForEach(r => r.Roles).Must(role => Roles.All.Contains(role)).WithMessage("Unknown role '{PropertyValue}'.");
    }
}

public sealed class UpdateStaffRolesRequestValidator : AbstractValidator<UpdateStaffRolesRequest>
{
    public UpdateStaffRolesRequestValidator()
    {
        RuleFor(r => r.Roles).NotNull().NotEmpty().WithMessage("Select at least one role.");
        RuleForEach(r => r.Roles).Must(role => Roles.All.Contains(role)).WithMessage("Unknown role '{PropertyValue}'.");
    }
}

public sealed class StaffQueryValidator : AbstractValidator<StaffQuery>
{
    public StaffQueryValidator()
    {
        RuleFor(q => q.Page).NotNull().SetValidator(new PageRequestValidator());
        RuleFor(q => q.Status)
            .Must(s => s is null or MembershipStatus.Active or MembershipStatus.Disabled or MembershipStatus.Invited)
            .WithMessage("Unknown status.");
        RuleFor(q => q.Search).MaximumLength(100);
    }
}
