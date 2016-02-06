using System;

namespace Logging
{
    public interface ILoggingService
    {
        void Log(LogLevel level, string message, Exception exception);
        void Warn(string message);
        void Warn(Exception exception, string message, params object[] args);
        void Fatal(string message);
        void Fatal(Exception exception, string message, params object[] args);
        void Info(string message);
        void Info(Exception exception, string message, params object[] args);
        void Error(string message);
        void Error(Exception exception, string message, params object[] args);
        void Trace(string message);
        void Trace(Exception exception, string message, params object[] args);
        void Debug(string message);
        void Debug(Exception exception, string message, params object[] args);
    }
}
