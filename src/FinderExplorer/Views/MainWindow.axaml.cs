// Copyright (c) Finder Explorer. All rights reserved.

using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using FinderExplorer.ViewModels;

namespace FinderExplorer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Sync DetailsPane actual width back to VM so GridLength stays up to date after splitter drag
        DetailsPaneBorder.SizeChanged += (_, e) =>
        {
            if (DataContext is MainWindowViewModel vm && vm.IsDetailsPaneVisible)
            {
                var w = e.NewSize.Width;
                if (w > 0 && Math.Abs(w - vm.DetailsPaneWidth) > 0.5)
                    vm.DetailsPaneWidth = w;
            }
        };

        Opened += (_, _) =>
        {
            ApplyMicaAltBackdrop();
            // Re-apply after initial composition passes; prevents backdrop reverting on startup.
            Dispatcher.UIThread.Post(ApplyMicaAltBackdrop, DispatcherPriority.Background);
            DispatcherTimer.RunOnce(ApplyMicaAltBackdrop, TimeSpan.FromMilliseconds(250));
            DispatcherTimer.RunOnce(ApplyMicaAltBackdrop, TimeSpan.FromMilliseconds(800));
            DispatcherTimer.RunOnce(ApplyMicaAltBackdrop, TimeSpan.FromMilliseconds(1600));
        };
        Activated += (_, _) => ApplyMicaAltBackdrop();
        // Subscribe to property changes to handle maximized state padding
        this.GetObservable(WindowStateProperty).Subscribe(new WindowStateObserver(this));
    }

    private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            if (e.ClickCount >= 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                SyncMaximizeIcon();
                e.Handled = true;
            }
            else
            {
                BeginMoveDrag(e);
            }
        }
    }

    private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        SyncMaximizeIcon();
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void SyncMaximizeIcon()
    {
        if (MaximizeIcon == null) return;
        MaximizeIcon.Data = WindowState == WindowState.Maximized
            ? Avalonia.Media.Geometry.Parse("M2.5,0.5 H9.5 V7.5 H7.5 M7.5,2.5 V9.5 H0.5 V2.5 Z")
            : Avalonia.Media.Geometry.Parse("M0.5,0.5 H9.5 V9.5 H0.5 Z");
    }

    private void OnWindowStateChanged(WindowState state)
    {
        SyncMaximizeIcon();
        // When maximized (fullscreen), add padding to prevent content from touching screen edges
        // This matches Files/WinUI behavior where maximized windows have a small safe area
        if (RootGrid is not null)
        {
            RootGrid.Margin = state is WindowState.Maximized or WindowState.FullScreen
                ? new Thickness(8, 8, 8, 8)
                : new Thickness(0);
        }

        ApplyMicaAltBackdrop();
    }

    private void FileItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedItem is { } item)
        {
            vm.OpenItemCommand.Execute(item);
        }
    }

    private void ApplyMicaAltBackdrop()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
            return;

        var darkMode = 1;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        var backdropType = (int)DwmSystemBackdropType.TabbedWindow;
        var result = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        if (result != 0)
        {
            // Older/limited Windows builds may reject TabbedWindow; fallback to standard Mica.
            backdropType = (int)DwmSystemBackdropType.MainWindow;
            _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
        }
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    private enum DwmSystemBackdropType
    {
        Auto = 0,
        None = 1,
        MainWindow = 2,
        TransientWindow = 3,
        TabbedWindow = 4
    }

    private sealed class WindowStateObserver(MainWindow window) : IObserver<WindowState>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(WindowState value) => window.OnWindowStateChanged(value);
    }
}
