using EmployeeData.Services;
using System.ComponentModel.DataAnnotations;

namespace EmployeeData.Dto
{
    public sealed class PaginationQuery
    {
        internal const int DefaultPageSize = 50;
        internal const int MaximumPageSize = 100;

        [Range(1, int.MaxValue)]
        public int PageNumber { get; set; } = 1;

        [Range(1, MaximumPageSize)]
        public int PageSize { get; set; } = DefaultPageSize;

        internal PageRequest ToPageRequest()
        {
            return new PageRequest(PageNumber, PageSize);
        }
    }
}
