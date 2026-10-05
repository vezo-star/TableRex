using FluentValidation;

namespace BookingPlatform.Application.Subscriptions;

public sealed class UpdateSubscriptionRequestValidator : AbstractValidator<UpdateSubscriptionRequest>
{
    public const int MaxOverride = 100_000;

    public UpdateSubscriptionRequestValidator()
    {
        RuleFor(r => r.PlanCode).NotEmpty().MaximumLength(40).Matches("^[A-Z][A-Z0-9_]{1,39}$").WithMessage("Unknown plan.");
        RuleFor(r => r.MaxVenuesOverride).InclusiveBetween(1, MaxOverride);
        RuleFor(r => r.MaxActiveStaffOverride).InclusiveBetween(1, MaxOverride);
        RuleFor(r => r.MaxResourcesPerVenueOverride).InclusiveBetween(1, MaxOverride);
    }
}

public sealed partial class UpdateBrandingRequestValidator : AbstractValidator<UpdateBrandingRequest>
{
    public const int LogoUrlMaxLength = 500;
    public const int IntroMaxLength = 1000;
    public const int TermsMaxLength = 2000;

    public UpdateBrandingRequestValidator()
    {
        RuleFor(r => r.PrimaryColor)
            .Must(c => string.IsNullOrWhiteSpace(c) || ColorPattern().IsMatch(c.Trim()))
            .WithMessage("Enter a colour as #RRGGBB, for example #1a73e8.");
        RuleFor(r => r.LogoUrl)
            .Must(u => string.IsNullOrWhiteSpace(u) || IsSafeLogoUrl(u.Trim()))
            .WithMessage($"Enter an https:// image URL of at most {LogoUrlMaxLength} characters.");
        RuleFor(r => r.BookingPageIntro).MaximumLength(IntroMaxLength);
        RuleFor(r => r.BookingTerms).MaximumLength(TermsMaxLength);
    }

    /// <summary>Absolute https URL without whitespace, quotes, angle brackets or parentheses, so it is inert in any attribute or CSS context.</summary>
    public static bool IsSafeLogoUrl(string url) =>
        url.Length <= LogoUrlMaxLength
        && !url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '"' or '\'' or '<' or '>' or '(' or ')' or '\\' or '`')
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    [System.Text.RegularExpressions.GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial System.Text.RegularExpressions.Regex ColorPattern();
}
