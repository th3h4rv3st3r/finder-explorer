// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using FinderExplorer.Core.Services;
using FinderExplorer.Native.Services;
using FinderExplorer.ViewModels;
using FinderExplorer.Views;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace FinderExplorer;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Remove Avalonia data validation to use CommunityToolkit validation
        BindingPlugins.DataValidators.RemoveAt(0);

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        // Load settings synchronously at startup (file is tiny, safe to block here)
        Services.GetRequiredService<ISettingsService>().LoadAsync().GetAwaiter().GetResult();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };

            // System Tray Lifecycle integration
            var lifecycle = Services.GetRequiredService<ILifecycleService>();

            mainWindow.Opened += (s, e) =>
            {
                var hwnd = mainWindow.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (hwnd != IntPtr.Zero)
                    lifecycle.InitializeTray(hwnd);
            };

            mainWindow.Closing += (s, e) =>
            {
                var appSettings = Services.GetRequiredService<ISettingsService>().Current;
                if (appSettings.MinimizeToTray)
                {
                    e.Cancel = true; // Prevent window destruction
                    lifecycle.HideToTray();
                }
                else
                {
                    lifecycle.RemoveTrayIcon();
                }
            };

            desktop.MainWindow = mainWindow;

            // Inject Windows system accent color into app resources
            mainWindow.Opened += (_, _) =>
            {
                try
                {
                    var platformSettings = mainWindow.PlatformSettings;
                    if (platformSettings != null)
                    {
                        var colors = platformSettings.GetColorValues();
                        var accent = colors.AccentColor1;
                        var accentBrush = new Avalonia.Media.SolidColorBrush(accent);

                        // Override accent brush resources directly
                        Resources["AccentFillColorDefaultBrush"] = accentBrush;
                        Resources["App.Theme.FillColorAttentionBrush"] = accentBrush;
                    }
                }
                catch
                {
                    // Fallback to default #60CDFF defined in WinUIColors.axaml
                }
            };

            // Force first-show visibility/activation to avoid "doesn't open on first launch".
            Dispatcher.UIThread.Post(() => EnsureMainWindowVisible(mainWindow), DispatcherPriority.Background);
            Dispatcher.UIThread.Post(() => EnsureMainWindowVisible(mainWindow), DispatcherPriority.Loaded);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void EnsureMainWindowVisible(MainWindow mainWindow)
    {
        if (!mainWindow.IsVisible)
            mainWindow.Show();

        if (mainWindow.WindowState == Avalonia.Controls.WindowState.Minimized)
            mainWindow.WindowState = Avalonia.Controls.WindowState.Normal;

        mainWindow.Activate();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Settings — singleton so all VMs share the same instance
        services.AddSingleton<ISettingsService, JsonSettingsService>();

        // Core services
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        services.AddSingleton<IDefaultFileManagerService, DefaultFileManagerService>();
        services.AddSingleton<ILifecycleService, LifecycleService>();

        // Native-backed services
        services.AddSingleton<ISearchService, EverythingSearchService>();
        services.AddSingleton<IArchiveService, NanaZipService>();
        services.AddSingleton<IThumbnailService, ThumbnailService>();
        services.AddSingleton<IFileDetailsService, FileDetailsService>();
        services.AddSingleton<IShellContextMenuService, ShellContextMenuService>();

        // Cloud Integrations
        services.AddSingleton<INextcloudService, NextcloudService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
    }
}
