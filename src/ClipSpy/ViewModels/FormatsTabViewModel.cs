using System.Collections.ObjectModel;
using ClipSpy.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipSpy.ViewModels;

public sealed partial class FormatsTabViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<FormatRow> _formats = [];

    [ObservableProperty]
    private int _totalFormats;

    public void Update(ClipboardSnapshot snapshot)
    {
        Formats.Clear();
        foreach (var f in snapshot.Formats)
        {
            Formats.Add(new FormatRow(
                f.FormatId,
                f.FormatIdHex,
                f.FormatName,
                f.DataSize,
                f.DataSizeFormatted,
                f.Memory.HandleHex,
                f.IsStandard));
        }
        TotalFormats = Formats.Count;
    }
}

public sealed record FormatRow(
    uint Id,
    string IdHex,
    string Name,
    long SizeBytes,
    string SizeFormatted,
    string HandleHex,
    bool IsStandard)
{
    public string IdDisplay => $"{Id} ({IdHex})";
}
