using Clipt.Plugins;

namespace Clipt.Models;

public sealed class PluginRegistrationInfo
{
    public required ICliptPlugin Plugin { get; init; }

    public required string Source { get; init; }

    public required bool IsRegistered { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed class PluginLoadFailureInfo
{
    public required string AssemblyPath { get; init; }

    public required string ErrorMessage { get; init; }
}
