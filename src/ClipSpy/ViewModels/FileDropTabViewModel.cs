using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ClipSpy.Models;
using ClipSpy.Native;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipSpy.ViewModels;

public sealed partial class FileDropTabViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<FileDropEntry> _files = [];

    [ObservableProperty]
    private bool _hasFiles;

    [ObservableProperty]
    private int _fileCount;

    [RelayCommand]
    private void OpenInExplorer(FileDropEntry? entry)
    {
        if (entry is null || !File.Exists(entry.FullPath))
            return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{entry.FullPath}\"",
            UseShellExecute = false,
        });
    }

    public void Update(ClipboardSnapshot snapshot)
    {
        Files.Clear();

        var hdropFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_HDROP);

        if (hdropFormat is null || hdropFormat.RawData.Length == 0)
        {
            HasFiles = false;
            FileCount = 0;
            return;
        }

        var paths = ParseFileDropFromSnapshot(snapshot);
        foreach (string path in paths)
        {
            var entry = CreateFileDropEntry(path);
            Files.Add(entry);
        }

        FileCount = Files.Count;
        HasFiles = FileCount > 0;
    }

    private static List<string> ParseFileDropFromSnapshot(ClipboardSnapshot snapshot)
    {
        var result = new List<string>();

        var hdropFormat = snapshot.Formats
            .FirstOrDefault(f => f.FormatId == ClipboardConstants.CF_HDROP);

        if (hdropFormat is null || hdropFormat.RawData.Length < 20)
            return result;

        byte[] data = hdropFormat.RawData;
        int fileOffset = BitConverter.ToInt32(data, 0);
        bool isWide = BitConverter.ToInt32(data, 16) != 0;

        if (fileOffset >= data.Length)
            return result;

        if (isWide)
        {
            int pos = fileOffset;
            while (pos + 1 < data.Length)
            {
                int start = pos;
                while (pos + 1 < data.Length && (data[pos] != 0 || data[pos + 1] != 0))
                    pos += 2;

                if (pos > start)
                    result.Add(System.Text.Encoding.Unicode.GetString(data, start, pos - start));

                pos += 2;
                if (pos + 1 >= data.Length || (data[pos] == 0 && data[pos + 1] == 0))
                    break;
            }
        }
        else
        {
            int pos = fileOffset;
            while (pos < data.Length)
            {
                int start = pos;
                while (pos < data.Length && data[pos] != 0)
                    pos++;

                if (pos > start)
                    result.Add(System.Text.Encoding.Default.GetString(data, start, pos - start));

                pos++;
                if (pos >= data.Length || data[pos] == 0)
                    break;
            }
        }

        return result;
    }

    private static FileDropEntry CreateFileDropEntry(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                return new FileDropEntry(
                    fi.FullName,
                    fi.Name,
                    fi.Length,
                    FormatSize(fi.Length),
                    fi.LastWriteTime,
                    false);
            }

            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                return new FileDropEntry(
                    di.FullName,
                    di.Name,
                    0,
                    "(directory)",
                    di.LastWriteTime,
                    true);
            }
        }
        catch
        {
            // fall through to unknown entry
        }

        return new FileDropEntry(path, Path.GetFileName(path), 0, "(unknown)", DateTime.MinValue, false);
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F2} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
    };
}

public sealed record FileDropEntry(
    string FullPath,
    string FileName,
    long SizeBytes,
    string SizeFormatted,
    DateTime LastModified,
    bool IsDirectory);
