using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using KROT.App.ViewModels;
using Forms = System.Windows.Forms;

namespace KROT.App;

public partial class MainWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Forms.NotifyIcon _trayIcon;
    private bool _allowClose;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.RequestOpenLogs += OnRequestOpenLogs;

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add(_viewModel.TrayOpenText, null, (_, _) => RestoreFromTray());
        contextMenu.Items.Add(_viewModel.TrayToggleText, null, async (_, _) => await ToggleFromTrayAsync());
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(_viewModel.TrayExitText, null, (_, _) => Close());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "KROT zapret",
            Visible = true,
            ContextMenuStrip = contextMenu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            IsEnabled = false;
            await _viewModel.StopForExitAsync();
            _allowClose = true;
            Close();
            return;
        }

        _viewModel.RequestOpenLogs -= OnRequestOpenLogs;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_OnClick(object sender, RoutedEventArgs e) => Hide();

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void OpenLogs_OnClick(object sender, RoutedEventArgs e) => _viewModel.OpenLogs();

    private void OnRequestOpenLogs(object? sender, EventArgs e)
    {
        var logWindow = new LogWindow(_viewModel.CurrentLanguage) { Owner = this };
        logWindow.ShowDialog();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async Task ToggleFromTrayAsync()
    {
        if (_viewModel.ToggleRuntimeCommand.CanExecute(null))
        {
            await _viewModel.ToggleRuntimeCommand.ExecuteAsync(null);
        }
    }
}
