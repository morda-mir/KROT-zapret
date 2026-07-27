using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using KROT.Infrastructure.Storage;

namespace KROT.App;

public partial class LogWindow
{
    private readonly bool _isRussian;

    public LogWindow(string language)
    {
        _isRussian = language.StartsWith("RU", StringComparison.OrdinalIgnoreCase);
        InitializeComponent();
        RefreshButton.Content = _isRussian ? "Обновить" : "Refresh";
        CopyButton.Content = _isRussian ? "Копировать" : "Copy";
        OpenFolderButton.Content = _isRussian ? "Открыть папку" : "Open folder";
        ClearOldButton.Content = _isRussian ? "Очистить старые" : "Clear old logs";
        RefreshLog();
    }

    private void Refresh_OnClick(object sender, RoutedEventArgs e) => RefreshLog();

    private void Copy_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(LogText.Text))
        {
            Clipboard.SetText(LogText.Text);
        }
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.ServiceLogsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.ServiceLogsDirectory}\"")
        {
            UseShellExecute = true
        });
    }

    private void ClearOld_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(AppPaths.LogsDirectory))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(AppPaths.LogsDirectory, "krot-*.log")
                     .Where(path => !path.EndsWith("krot-current.log", StringComparison.OrdinalIgnoreCase)))
        {
            File.Delete(file);
        }

        RefreshLog();
    }

    private void RefreshLog()
    {
        var serviceLog = ReadCurrentLog(AppPaths.ServiceLogsDirectory);
        var userLog = ReadCurrentLog(AppPaths.LogsDirectory);
        if (string.IsNullOrWhiteSpace(serviceLog) && string.IsNullOrWhiteSpace(userLog))
        {
            LogText.Text = _isRussian ? "Лог пока пуст." : "The log is empty.";
            return;
        }

        LogText.Text =
            "=== KROT SERVICE ===" + Environment.NewLine
            + serviceLog
            + Environment.NewLine
            + "=== KROT GUI ===" + Environment.NewLine
            + userLog;
    }

    private static string ReadCurrentLog(string directory)
    {
        var path = Path.Combine(directory, "krot-current.log");
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return "Log access denied.";
        }
        catch (IOException ex)
        {
            return $"Log read failed: {ex.Message}";
        }
    }
}
