using System;

namespace EmployeeData.Services
{
    public readonly struct PageRequest
    {
        public PageRequest(int pageNumber, int pageSize)
        {
            if (pageNumber < 1)
                throw new ArgumentOutOfRangeException(nameof(pageNumber));
            if (pageSize < 1)
                throw new ArgumentOutOfRangeException(nameof(pageSize));

            PageNumber = pageNumber;
            PageSize = pageSize;
        }

        public int PageNumber { get; }
        public int PageSize { get; }

        internal int Offset
        {
            get
            {
                var offset = (long)(PageNumber - 1) * PageSize;
                return offset > int.MaxValue ? int.MaxValue : (int)offset;
            }
        }
    }
}
