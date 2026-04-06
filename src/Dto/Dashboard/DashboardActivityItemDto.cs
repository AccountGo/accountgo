using System;

namespace Dto.Dashboard
{
    public class DashboardActivityItemDto
    {
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }
}
