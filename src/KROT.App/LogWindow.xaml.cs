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
        Directory.CreateDirectory(AppPaths.LogsDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogsDirectory}\"")
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
        var path = Path.Combine(AppPaths.LogsDirectory, "krot-current.log");
        LogText.Text = File.Exists(path)
            ? File.ReadAllText(path)
            : (_isRussian ? "Лог пока пуст." : "The log is empty.");
    }
}
