// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using FinderExplorer.Core.Services;
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
        // Core services
        services.AddSingleton<IFileSystemService, FileSystemService>();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
    }
}