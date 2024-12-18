using Newtonsoft.Json.Serialization;
using NLog;
using System.Diagnostics;

namespace MultiFunPlayer.Settings;

internal interface INLogNewtonsoftJsonLogger : ITraceWriter
{
    bool IsEnabled { get; set; }
}

internal interface INewtonsoftJsonLoggerManager
{
    bool IsEnabled { get; set; }
    INLogNewtonsoftJsonLogger GetLogger();
}

internal class NewtonsoftJsonLoggerManager : INewtonsoftJsonLoggerManager
{
    private NLogNewtonsoftJsonLogger _instance;
    private bool _isEnabled;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            if (_instance != null)
                _instance.IsEnabled = value;
        }
    }

    public INLogNewtonsoftJsonLogger GetLogger()
        => IsEnabled ? _instance ??= new NLogNewtonsoftJsonLogger() { IsEnabled = IsEnabled } : null;

    private class NLogNewtonsoftJsonLogger : INLogNewtonsoftJsonLogger
    {
        private static readonly Logger Logger = LogManager.GetLogger(nameof(Newtonsoft.Json));

        public TraceLevel LevelFilter => TraceLevel.Verbose;
        public bool IsEnabled { get; set; }

        public void Trace(TraceLevel level, string message, Exception exception)
        {
            if (!IsEnabled)
                return;

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