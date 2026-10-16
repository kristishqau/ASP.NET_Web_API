using System.Collections.Generic;

namespace EmployeeData.Services
{
    public sealed class PagedResult<T>
    {
        public PagedResult(IReadOnlyList<T> items, int totalCount)
        {
            Items = items;
            TotalCount = totalCount;
        }

        public IReadOnlyList<T> Items { get; }
        public int TotalCount { get; }
    }
}
