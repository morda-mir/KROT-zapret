using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KROT.App.ViewModels;
using KROT.Core.States;
using Forms = System.Windows.Forms;

namespace KROT.App;

public partial class MainWindow
{
    private const int WmSetIcon = 0x0080;
    private static readonly IntPtr IconSmall = IntPtr.Zero;
    private static readonly IntPtr IconBig = new(1);
    private static readonly IntPtr IconSmall2 = new(2);
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
        _trayIcon.BalloonTipClicked += OnUpdateBalloonTipClicked;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.RequestUpdateNotification += OnRequestUpdateNotification;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
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
        _viewModel.RequestUpdateNotification -= OnRequestUpdateNotification;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        SourceInitialized -= OnSourceInitialized;
        Loaded -= OnLoaded;
        _trayIcon.BalloonTipClicked -= OnUpdateBalloonTipClicked;
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

    private void HelpBackdrop_OnMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _viewModel.IsHelpOpen = false;
        e.Handled = true;
    }

    private void HelpPanel_OnMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e) => e.Handled = true;

    private void OnLoaded(object sender, RoutedEventArgs e) =>
        _viewModel.StartUpdateChecks();

    private void OnSourceInitialized(object? sender, EventArgs e) =>
        UpdateStatusIcons();

    private void OnRequestOpenLogs(object? sender, EventArgs e)
    {
        var logWindow = new LogWindow(_viewModel.CurrentLanguage) { Owner = this };
        logWindow.ShowDialog();
    }

    private void OnRequestUpdateNotification(object? sender, EventArgs e)
    {
        _trayIcon.ShowBalloonTip(
            10000,
            _viewModel.UpdateNotificationTitle,
            _viewModel.UpdateNotificationText,
            Forms.ToolTipIcon.Info);
    }

    private void OnUpdateBalloonTipClicked(object? sender, EventArgs e)
    {
        if (_viewModel.OpenAvailableUpdateCommand.CanExecute(null))
        {
            _viewModel.OpenAvailableUpdateCommand.Execute(null);
        }
    }

    public void RestoreFromTray()
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
        var trayIcon = active ? _trayOnIcon : _trayOffIcon;
        _trayIcon.Icon = trayIcon;
        Icon = active ? _windowOnIcon : _windowOffIcon;
        ApplyNativeWindowIcon(trayIcon);
    }

    private void ApplyNativeWindowIcon(System.Drawing.Icon icon)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        SendMessage(windowHandle, WmSetIcon, IconSmall, icon.Handle);
        SendMessage(windowHandle, WmSetIcon, IconBig, icon.Handle);
        SendMessage(windowHandle, WmSetIcon, IconSmall2, icon.Handle);
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

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter);
}
