using System.ComponentModel.DataAnnotations;

namespace HotelManagement.Application.Common;

/// <summary>Base query-string parameters for list endpoints: <c>?page=1&amp;pageSize=20</c>.</summary>
public class PageQuery
{
    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
