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

    [ObservableProperty]
    private FormatRow? _selectedFormatRow;

    [ObservableProperty]
    private string _selectedFormatDescription = string.Empty;

    partial void OnSelectedFormatRowChanged(FormatRow? value)
    {
        SelectedFormatDescription = value?.DetailedDescription ?? string.Empty;
    }

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

        SelectedFormatRow = null;
    }
}
