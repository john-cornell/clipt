using System.Windows;
using System.Windows.Controls;
using Clipt.Models;
using Clipt.ViewModels;

namespace Clipt.Views;

public partial class HexTabView : UserControl
{
    private bool _isSyncing;
    private bool _eventsSubscribed;

    public HexTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_eventsSubscribed)
            return;

        _eventsSubscribed = true;

        if (HexTextBox is not null)
            HexTextBox.SelectionChanged += HexTextBox_SelectionChanged;
        if (AsciiTextBox is not null)
            AsciiTextBox.SelectionChanged += AsciiTextBox_SelectionChanged;
    }

    private HexTabViewModel? ViewModel => DataContext as HexTabViewModel;

    private void HexTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
            return;

        if (HexTextBox is null || HexTextBox.SelectionLength <= 0)
            return;

        var vm = ViewModel;
        if (vm is null || vm.CurrentRawData.Length == 0)
            return;

        int bpr = Formatting.ClampBytesPerRow(vm.BytesPerRow);
        int hexLineWidth = Formatting.HexLineWidth(bpr);
        int lineLen = hexLineWidth + Environment.NewLine.Length;

        if (lineLen <= 0)
            return;

        (int startByte, int endByte) = ResolveByteRangeFromHex(
            HexTextBox.SelectionStart, HexTextBox.SelectionLength,
            bpr, lineLen, vm.CurrentRawData.Length);

        if (startByte < 0)
            return;

        int count = endByte - startByte + 1;
        vm.SelectedByteOffset = startByte;
        vm.SelectedByteCount = count;

        _isSyncing = true;
        try
        {
            HighlightAsciiRange(startByte, count, bpr);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void AsciiTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_isSyncing)
            return;

        if (AsciiTextBox is null || AsciiTextBox.SelectionLength <= 0)
            return;

        var vm = ViewModel;
        if (vm is null || vm.CurrentRawData.Length == 0)
            return;

        int bpr = Formatting.ClampBytesPerRow(vm.BytesPerRow);
        int asciiLineLen = bpr + Environment.NewLine.Length;

        if (asciiLineLen <= 0)
            return;

        (int startByte, int endByte) = ResolveByteRangeFromAscii(
            AsciiTextBox.SelectionStart, AsciiTextBox.SelectionLength,
            bpr, asciiLineLen, vm.CurrentRawData.Length);

        if (startByte < 0)
            return;

        int count = endByte - startByte + 1;
        vm.SelectedByteOffset = startByte;
        vm.SelectedByteCount = count;

        _isSyncing = true;
        try
        {
            HighlightHexRange(startByte, count, bpr);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private void HighlightHexRange(int startByte, int count, int bpr)
    {
        if (HexTextBox is null || string.IsNullOrEmpty(HexTextBox.Text))
            return;

        int hexLineWidth = Formatting.HexLineWidth(bpr);
        int hexLineLen = hexLineWidth + Environment.NewLine.Length;

        int startLine = startByte / bpr;
        int startCharInLine = Formatting.HexCharOffsetForByte(startByte % bpr, bpr);
        int hexStart = startLine * hexLineLen + startCharInLine;

        int endByte = startByte + count - 1;
        int endLine = endByte / bpr;
        int endCharInLine = Formatting.HexCharOffsetForByte(endByte % bpr, bpr) + 2;
        int hexEnd = endLine * hexLineLen + endCharInLine;

        int selLen = hexEnd - hexStart + 1;
        if (selLen <= 0 || hexStart < 0 || hexStart >= HexTextBox.Text.Length)
            return;

        selLen = Math.Min(selLen, HexTextBox.Text.Length - hexStart);
        HexTextBox.Select(hexStart, selLen);
    }

    private void HighlightAsciiRange(int startByte, int count, int bpr)
    {
        if (AsciiTextBox is null || string.IsNullOrEmpty(AsciiTextBox.Text))
            return;

        int asciiLineLen = bpr + Environment.NewLine.Length;
        int startLine = startByte / bpr;
        int startCharInLine = startByte % bpr;
        int asciiStart = startLine * asciiLineLen + startCharInLine;

        int endByte = startByte + count - 1;
        int endLine = endByte / bpr;
        int endCharInLine = endByte % bpr;
        int asciiEnd = endLine * asciiLineLen + endCharInLine;

        int selLen = asciiEnd - asciiStart + 1;
        if (selLen <= 0 || asciiStart < 0 || asciiStart >= AsciiTextBox.Text.Length)
            return;

        selLen = Math.Min(selLen, AsciiTextBox.Text.Length - asciiStart);
        AsciiTextBox.Select(asciiStart, selLen);
    }

    internal static (int startByte, int endByte) ResolveByteRangeFromHex(
        int selStart, int selLength, int bpr, int lineLen, int dataLength)
    {
        int startLine = selStart / lineLen;
        int startCharInLine = selStart % lineLen;
        int startByte = Formatting.AbsByteIndexFromHex(startLine, startCharInLine, bpr, dataLength);

        if (selLength <= 0)
            return (startByte, startByte);

        int endPos = selStart + selLength - 1;
        int endLine = endPos / lineLen;
        int endCharInLine = endPos % lineLen;
        int endByte = Formatting.AbsByteIndexFromHex(endLine, endCharInLine, bpr, dataLength);

        if (startByte < 0 && endByte < 0)
            return (-1, -1);

        if (startByte < 0) startByte = 0;
        if (endByte < 0) endByte = Math.Min(dataLength - 1, (endLine + 1) * bpr - 1);
        if (endByte >= dataLength) endByte = dataLength - 1;

        return (Math.Min(startByte, endByte), Math.Max(startByte, endByte));
    }

    internal static (int startByte, int endByte) ResolveByteRangeFromAscii(
        int selStart, int selLength, int bpr, int asciiLineLen, int dataLength)
    {
        int startLine = selStart / asciiLineLen;
        int startCharInLine = selStart % asciiLineLen;
        int startByte = Formatting.AbsByteIndexFromAscii(startLine, startCharInLine, bpr, dataLength);

        if (selLength <= 0)
            return (startByte, startByte);

        int endPos = selStart + selLength - 1;
        int endLine = endPos / asciiLineLen;
        int endCharInLine = endPos % asciiLineLen;
        int endByte = Formatting.AbsByteIndexFromAscii(endLine, endCharInLine, bpr, dataLength);

        if (startByte < 0 && endByte < 0)
            return (-1, -1);

        if (startByte < 0) startByte = 0;
        if (endByte < 0) endByte = Math.Min(dataLength - 1, (endLine + 1) * bpr - 1);
        if (endByte >= dataLength) endByte = dataLength - 1;

        return (Math.Min(startByte, endByte), Math.Max(startByte, endByte));
    }
}
