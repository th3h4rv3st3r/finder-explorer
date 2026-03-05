using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FinderExplorer.Core.Services;
using FinderExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FinderExplorer.Views.Controls
{
    public partial class FileList : UserControl
    {
        private readonly IShellContextMenuService? _shellContextMenuService;
        private bool _isMarqueeSelecting;
        private Point _marqueeStart;
        private IPointer? _marqueePointer;

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

            // For Nextcloud items or when shell menu fails, always use managed menu
            if (fileItem.IsNextcloudItem)
            {
                ShowManagedContextMenu();
            }
            else
            {
                ShowNativeContextMenu([fileItem.FullPath], e);
            }

            e.Handled = true;
        }

        private void FileList_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (e.Handled)
                return;

            var point = e.GetCurrentPoint(ItemsList);
            if (point.Properties.IsRightButtonPressed)
            {
                vm.SelectedItem = null;

                // For Nextcloud or Network paths, use managed menu
                if (vm.CurrentPath.StartsWith("nc://", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(vm.CurrentPath, @"\\Network", StringComparison.OrdinalIgnoreCase))
                {
                    ShowManagedContextMenu();
                }
                else
                {
                    ShowNativeContextMenu([vm.CurrentPath], e);
                }

                e.Handled = true;
                return;
            }

            if (!point.Properties.IsLeftButtonPressed)
                return;

            if (e.Source is Avalonia.Visual sourceVisual &&
                sourceVisual.FindAncestorOfType<ListBoxItem>() is not null)
            {
                return;
            }

            vm.SelectedItem = null;
            BeginMarqueeSelection(ClampToListBounds(point.Position), e.Pointer);
            e.Handled = true;
        }

        private void FileList_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_isMarqueeSelecting || _marqueePointer is null || _marqueePointer != e.Pointer)
                return;

            var current = ClampToListBounds(e.GetPosition(ItemsList));
            var rect = CreateSelectionRect(_marqueeStart, current);
            UpdateMarqueeVisual(rect);
            UpdateMarqueeSelection(rect);
            e.Handled = true;
        }

        private void FileList_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_isMarqueeSelecting || _marqueePointer is null || _marqueePointer != e.Pointer)
                return;

            EndMarqueeSelection();
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void FileList_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            EndMarqueeSelection();
        }

        private void BeginMarqueeSelection(Point start, IPointer pointer)
        {
            _isMarqueeSelecting = true;
            _marqueePointer = pointer;
            _marqueeStart = start;

            MarqueeSelectionBox.IsVisible = true;
            Canvas.SetLeft(MarqueeSelectionBox, start.X);
            Canvas.SetTop(MarqueeSelectionBox, start.Y);
            MarqueeSelectionBox.Width = 0;
            MarqueeSelectionBox.Height = 0;

            pointer.Capture(ItemsList);
        }

        private void EndMarqueeSelection()
        {
            _isMarqueeSelecting = false;
            _marqueePointer = null;
            MarqueeSelectionBox.IsVisible = false;
        }

        private void UpdateMarqueeVisual(Rect rect)
        {
            Canvas.SetLeft(MarqueeSelectionBox, rect.X);
            Canvas.SetTop(MarqueeSelectionBox, rect.Y);
            MarqueeSelectionBox.Width = rect.Width;
            MarqueeSelectionBox.Height = rect.Height;
        }

        private void UpdateMarqueeSelection(Rect selectionRect)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var selected = ItemsList
                .GetVisualDescendants()
                .OfType<ListBoxItem>()
                .Where(item => item.IsVisible)
                .Select(item => new
                {
                    Item = item,
                    Rect = GetItemRectRelativeToList(item)
                })
                .Where(x => x.Rect.HasValue && selectionRect.Intersects(x.Rect.Value))
                .OrderBy(x => x.Rect!.Value.Y)
                .Select(x => x.Item.DataContext as FileItemViewModel)
                .FirstOrDefault(item => item is not null);

            vm.SelectedItem = selected;
        }

        private Rect? GetItemRectRelativeToList(ListBoxItem item)
        {
            var topLeft = item.TranslatePoint(default, ItemsList);
            if (topLeft is null)
                return null;

            return new Rect(topLeft.Value, item.Bounds.Size);
        }

        private Point ClampToListBounds(Point point)
        {
            return new Point(
                Math.Clamp(point.X, 0, ItemsList.Bounds.Width),
                Math.Clamp(point.Y, 0, ItemsList.Bounds.Height));
        }

        private static Rect CreateSelectionRect(Point a, Point b)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            var width = Math.Abs(a.X - b.X);
            var height = Math.Abs(a.Y - b.Y);
            return new Rect(x, y, width, height);
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
                // --- Primary actions ---
                items.Add(new MenuItem { Header = "Open", Command = vm.OpenSelectedItemCommand });
                items.Add(new MenuItem { Header = "Open in New Tab", Command = vm.NewTabFromPathCommand, CommandParameter = vm.SelectedItem.FullPath });

                items.Add(new MenuItem { Header = "-" }); // Separator
                items.Add(new MenuItem { Header = "Cut", Command = vm.CutSelectedItemCommand, InputGesture = new KeyGesture(Key.X, KeyModifiers.Control) });
                items.Add(new MenuItem { Header = "Copy", Command = vm.CopySelectedItemCommand, InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) });
                items.Add(new MenuItem { Header = "Paste", Command = vm.PasteFromClipboardCommand, InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) });

                items.Add(new MenuItem { Header = "-" }); // Separator
                items.Add(new MenuItem { Header = "Rename", Command = vm.RenameSelectedItemCommand, InputGesture = new KeyGesture(Key.F2) });
                items.Add(new MenuItem { Header = "Delete", Command = vm.DeleteSelectedItemCommand, InputGesture = new KeyGesture(Key.Delete) });

                items.Add(new MenuItem { Header = "-" }); // Separator
                items.Add(new MenuItem { Header = "Copy path", Command = vm.CopyPathCommand });
                items.Add(new MenuItem { Header = "Share", Command = vm.ShareSelectedItemCommand });
                items.Add(new MenuItem { Header = "Properties", Command = vm.ShowPropertiesCommand });

                // --- Nextcloud-specific actions ---
                if (vm.SelectedItem.IsNextcloudItem)
                {
                    items.Add(new MenuItem { Header = "-" }); // Separator
                    items.Add(new MenuItem { Header = "Generate public link", Command = vm.GeneratePublicLinkCommand });
                    items.Add(new MenuItem { Header = "Generate internal link", Command = vm.GenerateInternalLinkCommand });
                }
            }
            else
            {
                // Background context menu (no item selected)
                items.Add(new MenuItem { Header = "Refresh", Command = vm.RefreshCommand, InputGesture = new KeyGesture(Key.F5) });
                items.Add(new MenuItem { Header = "New folder", Command = vm.CreateNewFolderCommand });
                items.Add(new MenuItem { Header = "Paste", Command = vm.PasteFromClipboardCommand, InputGesture = new KeyGesture(Key.V, KeyModifiers.Control) });

                items.Add(new MenuItem { Header = "-" }); // Separator

                // Sort By submenu
                var sortByItems = new List<MenuItem>
                {
                    new MenuItem { Header = "Name", Command = vm.SortByColumnCommand, CommandParameter = "Name" },
                    new MenuItem { Header = "Date modified", Command = vm.SortByColumnCommand, CommandParameter = "Modified" },
                    new MenuItem { Header = "Type", Command = vm.SortByColumnCommand, CommandParameter = "Type" },
                    new MenuItem { Header = "Size", Command = vm.SortByColumnCommand, CommandParameter = "Size" },
                };
                items.Add(new MenuItem { Header = "Sort by", ItemsSource = sortByItems });

                items.Add(new MenuItem { Header = "-" }); // Separator
                items.Add(new MenuItem { Header = "New tab", Command = vm.NewTabCommand });
            }

            // Filter out separator placeholders and convert to real separators
            var finalItems = new List<Control>();
            foreach (var item in items)
            {
                if (item.Header is string header && header == "-")
                    finalItems.Add(new Separator());
                else
                    finalItems.Add(item);
            }

            var menu = new ContextMenu
            {
                Placement = PlacementMode.Pointer,
                ItemsSource = finalItems
            };

            menu.Open(this);
        }
    }
}
