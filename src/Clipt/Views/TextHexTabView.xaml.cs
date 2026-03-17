using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Clipt.Models;
using Clipt.Native;
using Clipt.ViewModels;

namespace Clipt.Views;

public partial class TextHexTabView : UserControl
{
    private int _suppressionDepth;
    private bool _eventsSubscribed;

    private bool IsSuppressed => _suppressionDepth > 0;

    private static readonly HashSet<string> _textContentProps =
        [nameof(TextTabViewModel.UnicodeText), nameof(TextTabViewModel.AnsiText), nameof(TextTabViewModel.OemText)];

    private static readonly HashSet<string> _hexContentProps =
        [nameof(HexTabViewModel.HexColumn), nameof(HexTabViewModel.AsciiColumn)];

    public TextHexTabView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_eventsSubscribed)
            return;

        _eventsSubscribed = true;

        var unicodeBox = TextPane.UnicodeTextBox;
        var ansiBox = TextPane.AnsiTextBox;
        var oemBox = TextPane.OemTextBox;
        var hexBox = HexPane.HexTextBox;
        var asciiBox = HexPane.AsciiTextBox;

        if (unicodeBox is not null)
            unicodeBox.SelectionChanged += UnicodeTextBox_SelectionChanged;
        if (ansiBox is not null)
            ansiBox.SelectionChanged += AnsiTextBox_SelectionChanged;
        if (oemBox is not null)
            oemBox.SelectionChanged += OemTextBox_SelectionChanged;
        if (hexBox is not null)
            hexBox.SelectionChanged += HexTextBox_SelectionChanged;
        if (asciiBox is not null)
            asciiBox.SelectionChanged += AsciiTextBox_SelectionChanged;

        if (TextPane.DataContext is TextTabViewModel textVm)
            textVm.PropertyChanged += OnTextVmPropertyChanged;
        if (HexPane.DataContext is HexTabViewModel hexVm)
            hexVm.PropertyChanged += OnHexVmPropertyChanged;
    }

    private HexTabViewModel? HexViewModel => HexPane.DataContext as HexTabViewModel;

    private void OnTextVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && _textContentProps.Contains(e.PropertyName))
        {
            _suppressionDepth++;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (_suppressionDepth > 0)
                    _suppressionDepth--;
            });
        }
    }

    private void OnHexVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && _hexContentProps.Contains(e.PropertyName))
        {
            _suppressionDepth++;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (_suppressionDepth > 0)
                    _suppressionDepth--;
            });
        }
    }

    private bool TrySwitchHexFormat(uint targetFormatId)
    {
        var vm = HexViewModel;
        if (vm is null)
            return false;

        if (vm.SelectedFormat?.FormatId == targetFormatId)
            return true;

        var match = vm.AvailableFormats.FirstOrDefault(f => f.FormatId == targetFormatId);
        if (match is null)
            return false;

        vm.SelectedFormat = match;
        return true;
    }

    private void HandleTextPaneSelection(TextBox? textBox, uint targetFormatId, SelectionSource source, bool isUtf16)
    {
        if (IsSuppressed)
            return;

        if (textBox is null || textBox.SelectionLength <= 0)
            return;

        _suppressionDepth++;
        try
        {
            if (!TrySwitchHexFormat(targetFormatId))
                return;

            var (startByte, byteCount) = isUtf16
                ? Formatting.CharRangeToByteRangeUtf16(textBox.SelectionStart, textBox.SelectionLength)
                : Formatting.CharRangeToByteRangeSingleByte(textBox.SelectionStart, textBox.SelectionLength);

            var selection = new ByteRangeSelection(startByte, byteCount, source);
            if (selection.IsEmpty)
                return;

            ApplyCrossHighlight(selection);
        }
        finally
        {
            _suppressionDepth--;
        }
    }

    private void UnicodeTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        HandleTextPaneSelection(
            TextPane.UnicodeTextBox,
            ClipboardConstants.CF_UNICODETEXT,
            SelectionSource.UnicodeText,
            isUtf16: true);
    }

    private void AnsiTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        HandleTextPaneSelection(
            TextPane.AnsiTextBox,
            ClipboardConstants.CF_TEXT,
            SelectionSource.AnsiText,
            isUtf16: false);
    }

    private void OemTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        HandleTextPaneSelection(
            TextPane.OemTextBox,
            ClipboardConstants.CF_OEMTEXT,
            SelectionSource.OemText,
            isUtf16: false);
    }

    private void HexTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (IsSuppressed)
            return;

        var vm = HexViewModel;
        var hexBox = HexPane.HexTextBox;
        if (vm is null || hexBox is null || vm.CurrentRawData.Length == 0)
            return;

        int selStart = hexBox.SelectionStart;
        int selLength = hexBox.SelectionLength;
        string text = hexBox.Text;

        if (string.IsNullOrEmpty(text))
            return;

        int bpr = Formatting.ClampBytesPerRow(vm.BytesPerRow);
        int hexLineWidth = Formatting.HexLineWidth(bpr);
        int lineLen = hexLineWidth + Environment.NewLine.Length;

        if (lineLen <= 0)
            return;

        (int startByte, int endByte) = ResolveByteRangeFromHex(
            selStart, selLength, bpr, lineLen, vm.CurrentRawData.Length);

        if (startByte < 0)
        {
            ClearHexSelection(vm);
            return;
        }

        int count = endByte - startByte + 1;
        vm.SelectedByteOffset = startByte;
        vm.SelectedByteCount = count;

        _suppressionDepth++;
        try
        {
            ApplyCrossHighlight(
                new ByteRangeSelection(startByte, count, SelectionSource.HexColumn));
        }
        finally
        {
            _suppressionDepth--;
        }
    }

    private void AsciiTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (IsSuppressed)
            return;

        var vm = HexViewModel;
        var asciiBox = HexPane.AsciiTextBox;
        if (vm is null || asciiBox is null || vm.CurrentRawData.Length == 0)
            return;

        int selStart = asciiBox.SelectionStart;
        int selLength = asciiBox.SelectionLength;
        string text = asciiBox.Text;

        if (string.IsNullOrEmpty(text))
            return;

        int bpr = Formatting.ClampBytesPerRow(vm.BytesPerRow);
        int asciiLineLen = bpr + Environment.NewLine.Length;

        if (asciiLineLen <= 0)
            return;

        (int startByte, int endByte) = ResolveByteRangeFromAscii(
            selStart, selLength, bpr, asciiLineLen, vm.CurrentRawData.Length);

        if (startByte < 0)
        {
            ClearHexSelection(vm);
            return;
        }

        int count = endByte - startByte + 1;
        vm.SelectedByteOffset = startByte;
        vm.SelectedByteCount = count;

        _suppressionDepth++;
        try
        {
            ApplyCrossHighlight(
                new ByteRangeSelection(startByte, count, SelectionSource.AsciiColumn));
        }
        finally
        {
            _suppressionDepth--;
        }
    }

    private void ApplyCrossHighlight(ByteRangeSelection selection)
    {
        if (selection.IsEmpty)
            return;

        var vm = HexViewModel;
        int bpr = vm is not null ? Formatting.ClampBytesPerRow(vm.BytesPerRow) : 16;
        uint hexFormatId = vm?.SelectedFormat?.FormatId ?? 0;

        if (selection.Source != SelectionSource.HexColumn)
            HighlightHexRange(selection.StartByte, selection.Count, bpr);

        if (selection.Source != SelectionSource.AsciiColumn)
            HighlightAsciiRange(selection.StartByte, selection.Count, bpr);

        if (selection.Source != SelectionSource.UnicodeText && hexFormatId == ClipboardConstants.CF_UNICODETEXT)
            HighlightTextBox(TextPane.UnicodeTextBox, selection, isUtf16: true);

        if (selection.Source != SelectionSource.AnsiText && hexFormatId == ClipboardConstants.CF_TEXT)
            HighlightTextBox(TextPane.AnsiTextBox, selection, isUtf16: false);

        if (selection.Source != SelectionSource.OemText && hexFormatId == ClipboardConstants.CF_OEMTEXT)
            HighlightTextBox(TextPane.OemTextBox, selection, isUtf16: false);
    }

    private static void HighlightTextBox(TextBox? textBox, ByteRangeSelection selection, bool isUtf16)
    {
        if (textBox is null || string.IsNullOrEmpty(textBox.Text))
            return;

        var (charStart, charCount) = isUtf16
            ? Formatting.ByteRangeToCharRangeUtf16(selection.StartByte, selection.Count)
            : Formatting.ByteRangeToCharRangeSingleByte(selection.StartByte, selection.Count);

        if (charCount <= 0 || charStart < 0 || charStart >= textBox.Text.Length)
            return;

        charCount = Math.Min(charCount, textBox.Text.Length - charStart);
        textBox.Select(charStart, charCount);
    }

    private void HighlightHexRange(int startByte, int count, int bpr)
    {
        var hexBox = HexPane.HexTextBox;
        if (hexBox is null || string.IsNullOrEmpty(hexBox.Text))
            return;

        int hexLineWidth = Formatting.HexLineWidth(bpr);
        int hexLineLen = hexLineWidth + Environment.NewLine.Length;

        int startLine = startByte / bpr;
        int startCharInLine = Formatting.HexCharOffsetForByte(startByte % bpr, bpr);
        int hexStart = startLine * hexLineLen + startCharInLine;

        int endByte = startByte + count - 1;
        int endLine = endByte / bpr;
        int endCharInLine = Formatting.HexCharOffsetForByte(endByte % bpr, bpr) + 1;
        int hexEnd = endLine * hexLineLen + endCharInLine;

        int selLen = hexEnd - hexStart + 1;
        if (selLen <= 0 || hexStart < 0 || hexStart >= hexBox.Text.Length)
            return;

        selLen = Math.Min(selLen, hexBox.Text.Length - hexStart);
        hexBox.Select(hexStart, selLen);
    }

    private void HighlightAsciiRange(int startByte, int count, int bpr)
    {
        var asciiBox = HexPane.AsciiTextBox;
        if (asciiBox is null || string.IsNullOrEmpty(asciiBox.Text))
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
        if (selLen <= 0 || asciiStart < 0 || asciiStart >= asciiBox.Text.Length)
            return;

        selLen = Math.Min(selLen, asciiBox.Text.Length - asciiStart);
        asciiBox.Select(asciiStart, selLen);
    }

    private static (int startByte, int endByte) ResolveByteRangeFromHex(
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

    private static (int startByte, int endByte) ResolveByteRangeFromAscii(
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

    private static void ClearHexSelection(HexTabViewModel vm)
    {
        vm.SelectedByteOffset = -1;
        vm.SelectedByteCount = 0;
    }
}
