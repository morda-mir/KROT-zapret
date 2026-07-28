using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KROT.App.ViewModels;
using KROT.Core.States;
using Forms = System.Windows.Forms;

namespace KROT.App;

public partial class MainWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly System.Drawing.Icon _trayOffIcon;
    private readonly System.Drawing.Icon _trayOnIcon;
    private readonly ImageSource _windowOffIcon;
    private readonly ImageSource _windowOnIcon;
    private bool _allowClose;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.RequestOpenLogs += OnRequestOpenLogs;

        _trayOffIcon = LoadTrayIcon("assets/tray/krot-tray-off.ico");
        _trayOnIcon = LoadTrayIcon("assets/tray/krot-tray-on.ico");
        _windowOffIcon = LoadWindowIcon("assets/tray/krot-tray-off.ico");
        _windowOnIcon = LoadWindowIcon("assets/tray/krot-tray-on.ico");

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add(_viewModel.TrayOpenText, null, (_, _) => RestoreFromTray());
        contextMenu.Items.Add(_viewModel.TrayToggleText, null, async (_, _) => await ToggleFromTrayAsync());
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(_viewModel.TrayExitText, null, (_, _) => Close());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayOffIcon,
            Text = "KROT zapret",
            Visible = true,
            ContextMenuStrip = contextMenu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateStatusIcons();
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
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayOffIcon.Dispose();
        _trayOnIcon.Dispose();
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

    private void Minimize_OnClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState != WindowState.Minimized)
        {
            return;
        }

        ShowInTaskbar = false;
        Hide();
    }

    private void OpenLogs_OnClick(object sender, RoutedEventArgs e) => _viewModel.OpenLogs();

    private void OnRequestOpenLogs(object? sender, EventArgs e)
    {
        var logWindow = new LogWindow(_viewModel.CurrentLanguage) { Owner = this };
        logWindow.ShowDialog();
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.AppState))
        {
            UpdateStatusIcons();
        }
    }

    private void UpdateStatusIcons()
    {
        var active = _viewModel.AppState is AppState.Running or AppState.PartialFailure;
        _trayIcon.Icon = active ? _trayOnIcon : _trayOffIcon;
        Icon = active ? _windowOnIcon : _windowOffIcon;
    }

    private static System.Drawing.Icon LoadTrayIcon(string resourcePath)
    {
        var resource = Application.GetResourceStream(
            new Uri($"pack://application:,,,/KROT;component/{resourcePath}"))
            ?? throw new InvalidOperationException($"Missing tray icon resource: {resourcePath}");
        using var stream = resource.Stream;
        using var source = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)source.Clone();
    }

    private static ImageSource LoadWindowIcon(string resourcePath)
    {
        var resource = Application.GetResourceStream(
            new Uri($"pack://application:,,,/KROT;component/{resourcePath}"))
            ?? throw new InvalidOperationException($"Missing window icon resource: {resourcePath}");
        using var stream = resource.Stream;
        var image = BitmapFrame.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        image.Freeze();
        return image;
    }
}
