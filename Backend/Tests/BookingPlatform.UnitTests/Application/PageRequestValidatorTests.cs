using BookingPlatform.Application.Common.Pagination;

namespace BookingPlatform.UnitTests.Application;

public sealed class PageRequestValidatorTests
{
    private readonly PageRequestValidator _validator = new();

    [Fact]
    public void Defaults_are_valid()
    {
        var request = new PageRequest();

        _validator.Validate(request).IsValid.Should().BeTrue();
        request.Page.Should().Be(1);
        request.PageSize.Should().Be(25);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, PageRequest.MaxPageSize)]
    [InlineData(500, 50)]
    public void Accepts_values_within_bounds(int page, int pageSize) =>
        _validator.Validate(new PageRequest(page, pageSize)).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0, 25, nameof(PageRequest.Page))]
    [InlineData(-1, 25, nameof(PageRequest.Page))]
    [InlineData(1, 0, nameof(PageRequest.PageSize))]
    [InlineData(1, PageRequest.MaxPageSize + 1, nameof(PageRequest.PageSize))]
    public void Rejects_values_out_of_bounds(int page, int pageSize, string invalidProperty)
    {
        var result = _validator.Validate(new PageRequest(page, pageSize));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(invalidProperty);
    }

    [Theory]
    [InlineData(1, 25, 0)]
    [InlineData(2, 25, 25)]
    [InlineData(4, 10, 30)]
    public void Offset_skips_previous_pages(int page, int pageSize, int expectedOffset) =>
        new PageRequest(page, pageSize).Offset.Should().Be(expectedOffset);
}
