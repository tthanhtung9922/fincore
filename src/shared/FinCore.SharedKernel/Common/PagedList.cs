namespace FinCore.SharedKernel.Common;

public class PagedList<T>
{
    public IReadOnlyList<T> Items { get; }
    public int TotalCount { get; }
    public int PageSize { get; }
    public int PageNumber { get; }
    public bool HasNextPage => PageNumber * PageSize < TotalCount;
    public bool HasPreviousPage => PageNumber > 1;

    public PagedList(IReadOnlyList<T> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }
}
