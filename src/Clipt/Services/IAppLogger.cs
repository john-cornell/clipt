using Clipt.Models;

namespace Clipt.Services;

public interface IAppLogger
{
    AppLogLevel Level { get; }

    void SetLevel(AppLogLevel level);

    void Warn(string message);

    void Debug(string message);

    /// <summary>Always written to the log file (startup, shutdown, single-instance).</summary>
    void Info(string message);

    /// <summary>Always written to the log file (unhandled exceptions, fatal errors).</summary>
    void Error(string message, Exception? exception = null);
}
