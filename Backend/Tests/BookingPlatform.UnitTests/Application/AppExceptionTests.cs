using BookingPlatform.Application.Common.Errors;

namespace BookingPlatform.UnitTests.Application;

public sealed class AppExceptionTests
{
    [Fact]
    public void Not_found_and_conflict_carry_the_supplied_code_and_message()
    {
        var notFound = new NotFoundAppException("VENUE_NOT_FOUND", "Venue was not found.");
        var conflict = new ConflictAppException("RESOURCE_NOT_AVAILABLE", "Resource is not available.");

        notFound.Code.Should().Be("VENUE_NOT_FOUND");
        notFound.Message.Should().Be("Venue was not found.");
        conflict.Code.Should().Be("RESOURCE_NOT_AVAILABLE");
    }

    [Fact]
    public void Forbidden_uses_the_forbidden_code_and_a_safe_default_message()
    {
        var forbidden = new ForbiddenAppException();

        forbidden.Code.Should().Be(ErrorCodes.Forbidden);
        forbidden.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validation_exception_exposes_errors_with_validation_code()
    {
        var errors = new Dictionary<string, string[]> { ["partySize"] = ["Party size must be greater than 0."] };

        var exception = new ValidationAppException(errors);

        exception.Code.Should().Be(ErrorCodes.ValidationError);
        exception.Errors.Should().ContainKey("partySize");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Code_is_required(string code)
    {
        var act = () => new NotFoundAppException(code, "message");

        act.Should().Throw<ArgumentException>();
    }
}
