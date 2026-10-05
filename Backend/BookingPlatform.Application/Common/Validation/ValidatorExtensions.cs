using BookingPlatform.Application.Common.Errors;
using FluentValidation;

namespace BookingPlatform.Application.Common.Validation;

public static class ValidatorExtensions
{
    /// <summary>
    /// Validates <paramref name="instance"/> and throws <see cref="ValidationAppException"/> with errors grouped by property.
    /// </summary>
    public static async Task EnsureValidAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(failure => ToCamelCase(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());

        throw new ValidationAppException(errors);
    }

    /// <summary>Camel-cases every segment of a property path: <c>Rules.MaxPartySize</c> → <c>rules.maxPartySize</c>.</summary>
    private static string ToCamelCase(string propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? propertyName
            : string.Join('.', propertyName.Split('.').Select(segment =>
                segment.Length == 0 || char.IsLower(segment[0]) ? segment : char.ToLowerInvariant(segment[0]) + segment[1..]));
}
