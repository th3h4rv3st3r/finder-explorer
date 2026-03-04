// Copyright (c) Finder Explorer. All rights reserved.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FinderExplorer.ViewModels;

namespace FinderExplorer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Subscribe to property changes to handle maximized state padding
        this.GetObservable(WindowStateProperty).Subscribe(new WindowStateObserver(this));
    }

    private void OnWindowStateChanged(WindowState state)
    {
        // When maximized (fullscreen), add padding to prevent content from touching screen edges
        // This matches Files/WinUI behavior where maximized windows have a small safe area
        if (RootGrid is not null)
        {
            RootGrid.Margin = state == WindowState.Maximized
                ? new Thickness(8, 0, 8, 8)
                : new Thickness(0);
        }
    }

    private void FileItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedItem is { } item)
        {
            vm.OpenItemCommand.Execute(item);
        }
    }

    private sealed class WindowStateObserver(MainWindow window) : IObserver<WindowState>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(WindowState value) => window.OnWindowStateChanged(value);
    }
}