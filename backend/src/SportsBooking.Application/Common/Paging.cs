using FluentValidation;

namespace SportsBooking.Application.Common;

/// <summary>A page of a list (docs/stack.md: lists are always paginated and have a defined order). Pages start at 1.</summary>
public sealed record PageRequest(int Page = 1, int PageSize = PageRequest.DefaultPageSize)
{
    public const int DefaultPageSize = 20;

    public const int MaximumPageSize = 100;

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>One page of results and the total, so the client can show the number of pages.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed class PageRequestValidator : AbstractValidator<PageRequest>
{
    public PageRequestValidator()
    {
        RuleFor(request => request.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("A página tem de ser 1 ou mais.");

        RuleFor(request => request.PageSize)
            .InclusiveBetween(1, PageRequest.MaximumPageSize)
            .WithMessage($"O tamanho da página tem de estar entre 1 e {PageRequest.MaximumPageSize}.");
    }
}
