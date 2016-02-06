using System;

namespace Logging
{
    public class ApiException : Exception
    {
        public LogLevel Level { get; set; }

        public ApiException() : this(string.Empty)
        {

        }

        public ApiException(string message) : this(message, null)
        {

        }

        public ApiException(string message, Exception innerException) : this(message, innerException, LogLevel.Error)
        {

        }

        public ApiException(string message, Exception innerException, LogLevel level) : base(message, innerException)
        {
            Level = level;
        }
    }
}
