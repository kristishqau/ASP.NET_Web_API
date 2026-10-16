using System.Linq;

namespace EmployeeData.Services
{
    internal static class PaginationExtensions
    {
        internal static PagedResult<T> ToPage<T>(
            this IQueryable<T> query,
            PageRequest page)
        {
            var totalCount = query.Count();
            var items = query
                .Skip(page.Offset)
                .Take(page.PageSize)
                .ToList();

            return new PagedResult<T>(items, totalCount);
        }
    }
}
