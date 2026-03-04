// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
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
        }

        base.OnFrameworkInitializationCompleted();
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

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
    }
}
