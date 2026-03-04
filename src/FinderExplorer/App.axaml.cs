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
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
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

        // Native-backed services
        services.AddSingleton<ISearchService, EverythingSearchService>();
        services.AddSingleton<IArchiveService, NanaZipService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
    }
}
