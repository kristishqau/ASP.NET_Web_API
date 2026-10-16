using EmployeeData.Services;
using Microsoft.AspNetCore.Http;
using System.Globalization;

namespace EmployeeData.Helper
{
    internal static class PaginationHeaders
    {
        internal const string PageNumber = "X-Page-Number";
        internal const string PageSize = "X-Page-Size";
        internal const string TotalCount = "X-Total-Count";

        internal static void Add(
            HttpResponse response,
            PageRequest page,
            int totalCount)
        {
            response.Headers[PageNumber] = page.PageNumber.ToString(
                CultureInfo.InvariantCulture);
            response.Headers[PageSize] = page.PageSize.ToString(
                CultureInfo.InvariantCulture);
            response.Headers[TotalCount] = totalCount.ToString(
                CultureInfo.InvariantCulture);
        }
    }
}
