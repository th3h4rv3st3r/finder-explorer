using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using FinderExplorer.Core.Services;
using FinderExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace FinderExplorer.Views.Controls
{
    public partial class FileList : UserControl
    {
        private readonly IShellContextMenuService? _shellContextMenuService;
        private bool _isMarqueeSelecting;
        private Point _marqueeStart;
        private IPointer? _marqueePointer;
        private DispatcherTimer? _autoScrollTimer;
        private Point _currentPointerPosition;
        private ScrollViewer? _scrollViewer;
        private double _marqueeStartScrollOffset;

        private bool _isDraggingItem;
        private Point _dragStartPoint;

        public FileList()
        {
            InitializeComponent();
            _shellContextMenuService = App.Services.GetService<IShellContextMenuService>();

            AddHandler(DragDrop.DragOverEvent, FileList_DragOver);
            AddHandler(DragDrop.DropEvent, FileList_Drop);
            
            // Add keyboard navigation
            ItemsList.KeyDown += FileList_KeyDown;
        }

        private void FileList_KeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || vm.Items.Count == 0)
                return;

            if (e.Key == Key.Enter)
            {
                if (vm.HasSelection && vm.OpenSelectedItemCommand.CanExecute(null))
                {
                    vm.OpenSelectedItemCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Back)
            {
                if (vm.CanNavigateUp)
                {
                    vm.NavigateUpCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete)
            {
                if (vm.HasSelection && vm.DeleteSelectedItemCommand.CanExecute(null))
                {
                    vm.DeleteSelectedItemCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
            {
                int currentIndex = vm.SelectedItem != null ? vm.Items.IndexOf(vm.SelectedItem) : -1;
                if (currentIndex == -1)
                {
                    vm.SelectedItem = vm.Items.FirstOrDefault();
                    e.Handled = true;
                    return;
                }

                int newIndex = currentIndex;
                
                // Keep it simple for now: Up/Left = Previous, Down/Right = Next
                // (True 2D grid navigation requires knowing column counts, which changes dynamically)
                if (e.Key is Key.Up or Key.Left)
                {
                    newIndex = Math.Max(0, currentIndex - 1);
                }
                else if (e.Key is Key.Down or Key.Right)
                {
                    newIndex = Math.Min(vm.Items.Count - 1, currentIndex + 1);
                }

                if (newIndex != currentIndex)
                {
                    vm.SelectedItem = vm.Items[newIndex];
                    ItemsList.ScrollIntoView(vm.SelectedItem);
                }
                e.Handled = true;
            }
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

            // If the right-clicked item is not already selected, select only it
            if (!fileItem.IsSelected)
                vm.SelectedItem = fileItem;

            // Collect all selected paths for the context menu
            var selectedPaths = vm.Items
                .Where(i => i.IsSelected)
                .Select(i => i.FullPath)
                .ToList();
            if (selectedPaths.Count == 0)
                selectedPaths.Add(fileItem.FullPath);

            // For Nextcloud items or when shell menu fails, always use managed menu
            if (fileItem.IsNextcloudItem)
            {
                ShowManagedContextMenu();
            }
            else
            {
                ShowNativeContextMenu(selectedPaths, e);
            }

            e.Handled = true;
        }

        private void FileList_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            if (e.Handled)
                return;

            // Never start marquee/context actions when interacting with the scrollbar.
            if (IsPointerFromScrollBar(e))
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
                _isDraggingItem = true;
                _dragStartPoint = point.Position;
                return;
            }

            vm.SelectedItem = null;
            BeginMarqueeSelection(ClampToListBounds(point.Position), e.Pointer);
            e.Handled = true;
        }

        private async void FileList_PointerMoved(object? sender, PointerEventArgs e)
        {
            var current = ClampToListBounds(e.GetPosition(ItemsList));

            if (_isMarqueeSelecting && _marqueePointer != null && _marqueePointer == e.Pointer)
            {
                _currentPointerPosition = current;
                UpdateMarqueeFromPointer();
                e.Handled = true;
            }
            else if (_isDraggingItem && e.Pointer.Type == PointerType.Mouse && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                var diff = current - _dragStartPoint;
                if (Math.Abs(diff.X) > 3 || Math.Abs(diff.Y) > 3)
                {
                    _isDraggingItem = false;
                    await StartDragAsync(e);
                }
            }
        }

        private void FileList_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            _isDraggingItem = false;
            if (!_isMarqueeSelecting || _marqueePointer is null || _marqueePointer != e.Pointer)
                return;

            EndMarqueeSelection();
            e.Pointer.Capture(null);
            e.Handled = true;
        }

        private void FileList_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            _isDraggingItem = false;
            EndMarqueeSelection();
        }

        private async Task StartDragAsync(PointerEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;
            var selected = vm.Items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0 && vm.SelectedItem != null)
                selected.Add(vm.SelectedItem);
            if (selected.Count == 0) return;

            var data = new DataObject();
            data.Set(DataFormats.Files, selected.Select(i => i.FullPath));

            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
        }

        private void FileList_DragOver(object? sender, DragEventArgs e)
        {
            if (DataContext is not MainWindowViewModel) return;

            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                    ? DragDropEffects.Copy
                    : DragDropEffects.Move;
                e.Handled = true;
            }
            else
            {
                e.DragEffects = DragDropEffects.None;
            }
        }

        private async void FileList_Drop(object? sender, DragEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm) return;

            if (!e.Data.Contains(DataFormats.Files)) return;

            var files = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p != null).ToArray();
            if (files == null || files.Length == 0) return;

            var targetPath = vm.CurrentPath;

            // Use Visual Tree hit-testing to find the directory we dropped onto
            var point = e.GetPosition(ItemsList);
            var hitControl = ItemsList.InputHitTest(point) as Avalonia.Visual;

            if (hitControl != null)
            {
                var listBoxItem = hitControl.FindAncestorOfType<ListBoxItem>();
                if (listBoxItem?.DataContext is FileItemViewModel targetItem && targetItem.IsDirectory)
                {
                    targetPath = targetItem.FullPath;
                }
            }

            if (string.IsNullOrEmpty(targetPath)) return;

            var isCopy = e.DragEffects.HasFlag(DragDropEffects.Copy) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

            var fileSystem = App.Services.GetRequiredService<IFileSystemService>();
            foreach (var file in files)
            {
                if (file == null || string.Equals(file, targetPath, StringComparison.OrdinalIgnoreCase)) continue;
                
                try
                {
                    if (isCopy)
                    {
                        await fileSystem.CopyAsync(file, targetPath);
                    }
                    else
                    {
                        await fileSystem.MoveAsync(file, targetPath);
                    }
                }
                catch
                {
                    // Ignore individual file move/copy errors
                }
            }

            if (vm.RefreshCommand.CanExecute(null))
            {
                await vm.RefreshCommand.ExecuteAsync(null);
            }
        }

        private void BeginMarqueeSelection(Point start, IPointer pointer)
        {
            if (_scrollViewer == null)
                _scrollViewer = ItemsList.FindDescendantOfType<ScrollViewer>();

            if (_autoScrollTimer == null)
            {
                _autoScrollTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _autoScrollTimer.Tick += AutoScrollTimer_Tick;
            }

            _isMarqueeSelecting = true;
            _marqueePointer = pointer;
            _marqueeStart = start;
            _currentPointerPosition = start;

            if (_scrollViewer != null)
                _marqueeStartScrollOffset = _scrollViewer.Offset.Y;
            else
                _marqueeStartScrollOffset = 0;

            MarqueeSelectionBox.IsVisible = true;
            Canvas.SetLeft(MarqueeSelectionBox, start.X);
            Canvas.SetTop(MarqueeSelectionBox, start.Y);
            MarqueeSelectionBox.Width = 0;
            MarqueeSelectionBox.Height = 0;

            pointer.Capture(ItemsList);
            _autoScrollTimer.Start();
        }

        private void EndMarqueeSelection()
        {
            _autoScrollTimer?.Stop();
            _isMarqueeSelecting = false;
            _marqueePointer = null;
            MarqueeSelectionBox.IsVisible = false;
        }

        private void UpdateMarqueeFromPointer()
        {
            if (!_isMarqueeSelecting) return;

            var current = ClampToListBounds(_currentPointerPosition);
            var adjustedStart = _marqueeStart;
            
            if (_scrollViewer != null)
            {
                double offsetDiff = _scrollViewer.Offset.Y - _marqueeStartScrollOffset;
                adjustedStart = new Point(_marqueeStart.X, _marqueeStart.Y - offsetDiff);
            }
            
            var rect = CreateSelectionRect(adjustedStart, current);
            UpdateMarqueeVisual(rect);
            UpdateMarqueeSelection(rect);
        }

        private void AutoScrollTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isMarqueeSelecting || _scrollViewer == null) return;
            
            double scrollZoneHeight = 40; 
            double maxScrollSpeed = 25; 
            
            double scrollAmount = 0;
            
            if (_currentPointerPosition.Y < scrollZoneHeight)
            {
                double factor = 1.0 - Math.Max(0, _currentPointerPosition.Y / scrollZoneHeight);
                scrollAmount = -maxScrollSpeed * factor;
            }
            else if (_currentPointerPosition.Y > ItemsList.Bounds.Height - scrollZoneHeight)
            {
                double distanceToBottom = ItemsList.Bounds.Height - _currentPointerPosition.Y;
                double factor = 1.0 - Math.Max(0, distanceToBottom / scrollZoneHeight);
                scrollAmount = maxScrollSpeed * factor;
            }
            
            if (Math.Abs(scrollAmount) > 0.5)
            {
                double currentOffset = _scrollViewer.Offset.Y;
                double maxOffset = Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
                double newOffset = Math.Clamp(currentOffset + scrollAmount, 0, maxOffset);
                
                if (Math.Abs(newOffset - currentOffset) > 0.1)
                {
                    _scrollViewer.Offset = new Avalonia.Vector(_scrollViewer.Offset.X, newOffset);
                    UpdateMarqueeFromPointer();
                }
            }
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

            var intersectedItems = ItemsList
                .GetVisualDescendants()
                .OfType<ListBoxItem>()
                .Where(item => item.IsVisible)
                .Select(item => new
                {
                    Item = item,
                    FileRow = item.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("file-row"))
                })
                .Where(x => x.FileRow != null)
                .Select(x => new
                {
                    x.Item,
                    Rect = GetItemRectRelativeToList(x.FileRow!)
                })
                .Where(x => x.Rect.HasValue && selectionRect.Intersects(x.Rect.Value))
                .OrderBy(x => x.Rect!.Value.Y)
                .Select(x => x.Item.DataContext as FileItemViewModel)
                .Where(item => item is not null)
                .Cast<FileItemViewModel>()
                .ToList();

            var intersectedSet = new HashSet<FileItemViewModel>(intersectedItems);

            foreach (var item in vm.Items)
            {
                bool shouldBeSelected = intersectedSet.Contains(item);
                if (item.IsSelected != shouldBeSelected)
                {
                    item.IsSelected = shouldBeSelected;
                }
            }

            vm.SelectedItem = intersectedItems.FirstOrDefault();
        }

        private Rect? GetItemRectRelativeToList(Avalonia.Visual itemVisual)
        {
            var topLeft = itemVisual.TranslatePoint(default, ItemsList);
            if (topLeft is null)
                return null;

            return new Rect(topLeft.Value, itemVisual.Bounds.Size);
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

        private static bool IsPointerFromScrollBar(PointerEventArgs e)
        {
            if (e.Source is not Avalonia.Visual sourceVisual)
                return false;

            if (sourceVisual is ScrollBar or Track or Thumb)
                return true;

            return sourceVisual.FindAncestorOfType<ScrollBar>() is not null;
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
