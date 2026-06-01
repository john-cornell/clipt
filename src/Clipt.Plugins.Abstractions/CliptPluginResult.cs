namespace Clipt.Plugins;

public sealed class CliptPluginResult
{
    public required bool Success { get; init; }

    public string? Message { get; init; }

    /// <summary>
    /// When set, the host writes this text to the system clipboard (as if the user pasted it).
    /// </summary>
    public string? OutputClipboardText { get; init; }

    public static CliptPluginResult Ok(string outputClipboardText, string? message = null) =>
        new() { Success = true, OutputClipboardText = outputClipboardText, Message = message };

    public static CliptPluginResult Fail(string message) =>
        new() { Success = false, Message = message };
}
