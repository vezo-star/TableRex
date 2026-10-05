using FluentValidation;

namespace BookingPlatform.Application.Common.Pagination;

public sealed record PageRequest(int Page = PageRequest.DefaultPage, int PageSize = PageRequest.DefaultPageSize)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public int Offset => (Page - 1) * PageSize;
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1);

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, PageRequest.MaxPageSize);
    }
}
