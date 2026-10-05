using BookingPlatform.Application.Common.Errors;
using BookingPlatform.Application.Common.Pagination;
using BookingPlatform.Application.Common.Validation;

namespace BookingPlatform.UnitTests.Application;

public sealed class ValidatorExtensionsTests
{
    private readonly PageRequestValidator _validator = new();

    [Fact]
    public async Task Valid_instance_does_not_throw()
    {
        var act = () => _validator.EnsureValidAsync(new PageRequest(), TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Invalid_instance_throws_validation_exception_with_camel_case_keys()
    {
        var act = () => _validator.EnsureValidAsync(new PageRequest(0, 1000), TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<ValidationAppException>()).Which;
        exception.Code.Should().Be(ErrorCodes.ValidationError);
        exception.Errors.Keys.Should().BeEquivalentTo("page", "pageSize");
        exception.Errors["pageSize"].Should().NotBeEmpty();
    }
}
