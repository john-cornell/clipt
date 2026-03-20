using Clipt.Models;

namespace Clipt.Services;

public interface IAppLogger
{
    AppLogLevel Level { get; }

    void SetLevel(AppLogLevel level);

    void Warn(string message);

    void Debug(string message);
}
