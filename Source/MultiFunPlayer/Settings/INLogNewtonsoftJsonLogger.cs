using Newtonsoft.Json.Serialization;
using NLog;
using System.Diagnostics;

namespace MultiFunPlayer.Settings;

internal interface INLogNewtonsoftJsonLogger : ITraceWriter;

internal interface INewtonsoftJsonLoggerManager
{
    INLogNewtonsoftJsonLogger GetLogger();

    void SuspendLogging();
    void ResumeLogging();
    bool IsLoggingEnabled();
}

internal class NewtonsoftJsonLoggerManager : INewtonsoftJsonLoggerManager
{
    private NLogNewtonsoftJsonLogger _instance;
    private bool _enabled;

    public INLogNewtonsoftJsonLogger GetLogger()
        => IsLoggingEnabled() ? _instance ??= new NLogNewtonsoftJsonLogger() : null;

    public bool IsLoggingEnabled() => _enabled;
    public void ResumeLogging() => _enabled = true;
    public void SuspendLogging() => _enabled = false;

    private class NLogNewtonsoftJsonLogger : INLogNewtonsoftJsonLogger
    {
        private static readonly Logger Logger = LogManager.GetLogger(nameof(Newtonsoft.Json));

        public TraceLevel LevelFilter => TraceLevel.Verbose;

        public void Trace(TraceLevel level, string message, Exception exception)
        {
            var logLevel = level switch
            {
                TraceLevel.Error => LogLevel.Error,
                TraceLevel.Warning => LogLevel.Warn,
                TraceLevel.Info => LogLevel.Info,
                TraceLevel.Off => LogLevel.Off,
                _ => LogLevel.Trace,
            };

            if (exception != null)
                Logger.Log(logLevel, exception, message);
            else
                Logger.Log(logLevel, message);
        }
    }
}