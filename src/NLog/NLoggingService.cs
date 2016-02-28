using NLog;
using System;

namespace Logging
{
    public class NLoggingService : ILoggingService
    {
        Logger _logger;

        public NLoggingService()
        {
            _logger = LogManager.GetLogger("databaseLogger");
        }

        public void Log(LogLevel level, string message, Exception exception)
        {
            switch (level)
            {
                case LogLevel.Info:
                    Info(exception, message);
                    break;
                case LogLevel.Warn:
                    Warn(exception, message);
                    break;
                case LogLevel.Error:
                    Error(exception, message);
                    break;
                case LogLevel.Fatal:
                    Fatal(exception, message);
                    break;
                case LogLevel.Debug:
                    Debug(exception, message);
                    break;
                case LogLevel.Trace:
                    Trace(exception, message);
                    break;
            }
        }

        public void Warn(string message)
        {
            _logger.Warn(message);
        }

        public void Warn(Exception exception, string message, params object[] args)
        {
            _logger.Warn(exception, message, args);
        }

        public void Fatal(string message)
        {
            _logger.Fatal(message);
        }

        public void Fatal(Exception exception, string message, params object[] args)
        {
            _logger.Fatal(exception, message);
        }

        public void Info(string message)
        {
            _logger.Fatal(message);
        }

        public void Info(Exception exception, string message, params object[] args)
        {
            _logger.Info(exception, message);
        }

        public void Error(string message)
        {
            _logger.Error(message);
        }

        public void Error(Exception exception, string message, params object[] args)
        {
            _logger.Error(exception, message);
        }

        public void Trace(string message)
        {
            _logger.Trace(message);
        }

        public void Trace(Exception exception, string message, params object[] args)
        {
            _logger.Trace(exception, message);
        }

        public void Debug(string message)
        {
            _logger.Debug(message);
        }

        public void Debug(Exception exception, string message, params object[] args)
        {
            _logger.Debug(exception, message);
        }
    }
}
