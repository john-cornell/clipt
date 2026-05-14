using Clipt.Models;

namespace Clipt.Services;

/// <summary>Shown when a clipboard format exceeds the per-format capture cap and mode is Ask.</summary>
public interface IClipboardFormatOversizePrompt
{
    ClipboardFormatOversizeReply Prompt(uint formatId, string formatName, long dataSizeBytes, long capBytes);
}
