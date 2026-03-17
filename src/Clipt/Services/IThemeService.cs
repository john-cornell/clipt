using Clipt.Models;

namespace Clipt.Services;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    void ApplyTheme(AppTheme theme);
    AppTheme LoadSavedTheme();
}
