using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KROT.Diagnostics.FieldTesting;

namespace KROT.FieldTest;

public partial class MainWindow
{
    private readonly FieldTestRunner _runner = new();
    private readonly FieldTestReportWriter _writer = new();
    private CancellationTokenSource? _cancellation;
    private string? _lastReportPath;

    public MainWindow()
    {
        InitializeComponent();
        ShowNetworkState();
    }

    protected override void OnClosed(EventArgs e)
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    private async void Run_OnClick(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        OpenFolderButton.IsEnabled = false;
        ProgressText.Clear();
        ReportPathText.Text = string.Empty;
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<FieldTestProgress>(
            item => AppendProgress($"{DateTime.Now:HH:mm:ss}  {item.Message}"));

        try
        {
            AppendProgress("Начинаем безопасную проверку. Содержимое ответов не сохраняется.");
            var report = await _runner.RunAsync(progress, _cancellation.Token);
            _lastReportPath = FieldTestPaths.CreateReportPath();
            await _writer.WriteAsync(report, _lastReportPath, _cancellation.Token);
            ReportPathText.Text = _lastReportPath;
            OpenFolderButton.IsEnabled = true;
            AppendProgress(report.Network.VpnLikely
                ? "Готово. Обнаружен VPN или системный прокси: отчёт нельзя считать проверкой провайдера."
                : "Готово. VPN не обнаружен: отчёт подходит для анализа подключения провайдера.");
        }
        catch (OperationCanceledException)
        {
            AppendProgress("Проверка отменена.");
        }
        catch (Exception ex)
        {
            AppendProgress($"Ошибка инструмента: {ex.GetType().Name}");
        }
        finally
        {
            _cancellation?.Dispose();
            _cancellation = null;
            RunButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => _cancellation?.Cancel();

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var directory = _lastReportPath == null
            ? FieldTestPaths.ReportsDirectory
            : Path.GetDirectoryName(_lastReportPath) ?? FieldTestPaths.ReportsDirectory;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"")
        {
            UseShellExecute = true
        });
    }

    private void ShowNetworkState()
    {
        var network = new NetworkEnvironmentInspector().Inspect();
        if (network.VpnLikely)
        {
            NetworkBanner.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(55, 43, 26));
            NetworkStatusText.Text =
                $"Обнаружен VPN, туннель или системный прокси. Активных адаптеров: {network.ActiveAdapterCount}. " +
                "Проверку можно запустить для smoke-test, но для честного результата отключите VPN.";
        }
        else
        {
            NetworkBanner.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(29, 53, 42));
            NetworkStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(80, 216, 144));
            NetworkStatusText.Text =
                $"VPN и системный прокси не обнаружены. Активных адаптеров: {network.ActiveAdapterCount}.";
        }
    }

    private void AppendProgress(string message)
    {
        ProgressText.AppendText(message + Environment.NewLine);
        ProgressText.ScrollToEnd();
    }
}

