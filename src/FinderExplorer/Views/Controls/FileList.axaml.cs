using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FinderExplorer.Core.Services;
using FinderExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace FinderExplorer.Views.Controls
{
    public partial class FileList : UserControl
    {
        private readonly IShellContextMenuService? _shellContextMenuService;

        public FileList()
        {
            InitializeComponent();
            _shellContextMenuService = App.Services.GetService<IShellContextMenuService>();
        }

        private void FileItem_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && sender is Control control && control.DataContext is FinderExplorer.ViewModels.FileItemViewModel fileItem)
            {
                vm.OpenItemCommand.Execute(fileItem);
            }
        }

        private void FileItem_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (sender is not Control control || control.DataContext is not FileItemViewModel fileItem)
                return;

            if (!e.GetCurrentPoint(control).Properties.IsRightButtonPressed)
                return;

            vm.SelectedItem = fileItem;
            ShowNativeContextMenu([fileItem.FullPath], e);
            e.Handled = true;
        }

        private void FileList_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (e.Handled || !e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
                return;

            vm.SelectedItem = null;
            ShowNativeContextMenu([vm.CurrentPath], e);
            e.Handled = true;
        }

        private void ShowNativeContextMenu(IReadOnlyList<string> paths, PointerPressedEventArgs e)
        {
            var shown = false;

            if (_shellContextMenuService is not null && paths.Count > 0 && TopLevel.GetTopLevel(this) is TopLevel topLevel)
            {
                var hwnd = topLevel.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                var point = this.PointToScreen(e.GetPosition(this));
                shown = _shellContextMenuService.TryShowContextMenu(paths, hwnd, point.X, point.Y);
            }

            if (!shown)
                ShowManagedContextMenu();
        }

        private void ShowManagedContextMenu()
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var items = new List<MenuItem>();

            if (vm.SelectedItem is not null)
            {
                items.Add(new MenuItem { Header = "Open", Command = vm.OpenSelectedItemCommand });
                items.Add(new MenuItem { Header = "Delete", Command = vm.DeleteSelectedItemCommand });
            }
            else
            {
                items.Add(new MenuItem { Header = "Refresh", Command = vm.RefreshCommand });
                items.Add(new MenuItem { Header = "New tab", Command = vm.NewTabCommand });
            }

            var menu = new ContextMenu
            {
                Placement = PlacementMode.Pointer,
                ItemsSource = items
            };

            menu.Open(this);
        }
    }
}
