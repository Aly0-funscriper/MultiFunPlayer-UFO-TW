using NLog;
using System.Collections.Concurrent;

namespace MultiFunPlayer.UI;

internal interface IStyletLogger : Stylet.Logging.ILogger
{
    bool IsEnabled { get; set; }
}

internal interface IStyletLoggerManager
{
    bool IsEnabled { get; set; }
    IStyletLogger GetLogger(string name);
}

internal sealed class StyletLoggerManager : IStyletLoggerManager
{
    private readonly ConcurrentDictionary<string, IStyletLogger> _loggers;
    private bool _isEnabled;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            foreach (var (_, logger) in _loggers)
                logger.IsEnabled = value;
        }
    }

    public StyletLoggerManager()
        => _loggers = new ConcurrentDictionary<string, IStyletLogger>();

    public IStyletLogger GetLogger(string name)
        => _loggers.GetOrAdd(name, name => new NLogStyletLogger(name) { IsEnabled = IsEnabled });

    private sealed class NLogStyletLogger(string name) : IStyletLogger
    {
        private readonly Logger _logger = LogManager.GetLogger(name);

        public bool IsEnabled { get; set; }

        public void Error(Exception exception, string message = null)
        {
            if (IsEnabled)
                _logger.Error(exception, message);
        }

        public void Info(string format, params object[] args)
        {
            if (IsEnabled)
                _logger.Info(format, args);
        }

        public void Warn(string format, params object[] args)
        {
            if (IsEnabled)
                _logger.Warn(format, args);
        }
    }
}
