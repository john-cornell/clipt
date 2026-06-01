using System.IO;
using Clipt.Models;

namespace Clipt.Services;

public sealed class AppLogger : IAppLogger
{
    private readonly ISettingsService _settings;
    private readonly object _fileLock = new();
    private readonly string _logPath;
    private AppLogLevel _level;

    public AppLogger(ISettingsService settings)
    {
        _settings = settings;
        _level = _settings.LoadLogLevel();
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipt");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, "clipt.log");
    }

    public AppLogLevel Level => _level;

    public void SetLevel(AppLogLevel level)
    {
        if (!Enum.IsDefined(level))
            level = AppLogLevel.Off;

        _level = level;
        _settings.SaveLogLevel(level);
    }

    public void Warn(string message)
    {
        if (_level < AppLogLevel.Warn)
            return;

        WriteLine("WARN", message);
    }

    public void Debug(string message)
    {
        if (_level < AppLogLevel.Debug)
            return;

        WriteLine("DEBUG", message);
    }

    public void Info(string message) => WriteLine("INFO", message);

    public void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            WriteLine("ERROR", message);
            return;
        }

        WriteLine("ERROR", $"{message}{Environment.NewLine}{exception}");
    }

    private void WriteLine(string severity, string message)
    {
        string line =
            $"{DateTime.UtcNow:O}\t{severity}\t{message}{Environment.NewLine}";

        lock (_fileLock)
        {
            try
            {
                File.AppendAllText(_logPath, line);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
