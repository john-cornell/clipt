using System.Collections.ObjectModel;
using Clipt.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Clipt.ViewModels;

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
