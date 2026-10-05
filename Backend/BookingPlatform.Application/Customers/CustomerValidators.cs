using BookingPlatform.Application.Bookings;
using FluentValidation;

namespace BookingPlatform.Application.Customers;

public static class CustomerRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, string> firstName, Func<T, string?> lastName, Func<T, string?> email,
        Func<T, string?> phone, Func<T, string?> notes)
    {
        validator.RuleFor(r => firstName(r))
            .Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length <= 100)
            .OverridePropertyName("firstName")
            .WithMessage("First name is required (1-100 characters).");
        validator.RuleFor(r => lastName(r)).MaximumLength(100).OverridePropertyName("lastName");
        validator.RuleFor(r => email(r))
            .MaximumLength(254)
            .EmailAddress()
            .When(r => !string.IsNullOrWhiteSpace(email(r)))
            .OverridePropertyName("email");
        validator.RuleFor(r => phone(r)).MaximumLength(40).OverridePropertyName("phone");
        validator.RuleFor(r => phone(r))
            .Must(p => BookingService.NormalizePhone(p) is not null)
            .When(r => !string.IsNullOrWhiteSpace(phone(r)))
            .OverridePropertyName("phone")
            .WithMessage("Phone must contain 4-20 digits.");
        validator.RuleFor(r => notes(r)).MaximumLength(2000).OverridePropertyName("notes");
    }
}

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator() =>
        CustomerRules.Apply(this, r => r.FirstName, r => r.LastName, r => r.Email, r => r.Phone, r => r.Notes);
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator() =>
        CustomerRules.Apply(this, r => r.FirstName, r => r.LastName, r => r.Email, r => r.Phone, r => r.Notes);
}

public sealed class CustomerListQueryValidator : AbstractValidator<CustomerListQuery>
{
    public CustomerListQueryValidator()
    {
        RuleFor(q => q.Page.Page).GreaterThanOrEqualTo(1).OverridePropertyName("page");
        RuleFor(q => q.Page.PageSize).InclusiveBetween(1, Common.Pagination.PageRequest.MaxPageSize).OverridePropertyName("pageSize");
        RuleFor(q => q.Q).MaximumLength(100);
    }
}
