using System;

namespace Core.Utilities
{
    public interface IDateTimeProvider
    {
        public DateTime Now { get; }
    }
    public class DateTimeProvider : IDateTimeProvider
    {
        public DateTime Now => DateTime.UtcNow;
    }
}
