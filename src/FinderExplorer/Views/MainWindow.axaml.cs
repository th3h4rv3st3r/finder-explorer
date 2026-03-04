// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia.Controls;
using Avalonia.Input;
using FinderExplorer.ViewModels;

namespace FinderExplorer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void FileItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.SelectedItem is { } item)
        {
            vm.OpenItemCommand.Execute(item);
        }
    }
}