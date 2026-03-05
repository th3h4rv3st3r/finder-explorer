using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FinderExplorer.ViewModels;
using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using LibVlcMedia = LibVLCSharp.Shared.Media;

namespace FinderExplorer.Views.Controls
{
    public partial class DetailsPane : UserControl
    {
        private const double PreviewPanStep = 96;
        private static readonly Dictionary<string, (double Width, double Height)> ImageNaturalSizeCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object ImageNaturalSizeCacheSync = new();
        private static readonly object LibVlcInitSync = new();
        private static bool _libVlcInitialized;
        private MainWindowViewModel? _subscribedVm;
        private double _imageNaturalWidth;
        private double _imageNaturalHeight;
        private LibVLC? _libVlc;
        private MediaPlayer? _mediaPlayer;
        private string? _activeMediaPath;

        public DetailsPane()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            DetachedFromVisualTree += (_, _) =>
            {
                UnsubscribeFromViewModel(DataContext as MainWindowViewModel);
                DisposeMediaPreview();
            };
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_subscribedVm is not null)
                UnsubscribeFromViewModel(_subscribedVm);

            if (DataContext is MainWindowViewModel vm)
                SubscribeToViewModel(vm);
        }

        private void SubscribeToViewModel(MainWindowViewModel vm)
        {
            vm.PropertyChanged -= ViewModelOnPropertyChanged;
            vm.PropertyChanged += ViewModelOnPropertyChanged;
            _subscribedVm = vm;
            LoadNaturalImageSize(vm.ImagePreviewFilePath);
            UpdateMediaPreview(vm.MediaPreviewFilePath);
            Dispatcher.UIThread.Post(UpdateImagePreviewLayout, DispatcherPriority.Background);
        }

        private void UnsubscribeFromViewModel(MainWindowViewModel? vm)
        {
            if (vm is null)
                return;

            vm.PropertyChanged -= ViewModelOnPropertyChanged;
            if (ReferenceEquals(_subscribedVm, vm))
                _subscribedVm = null;
        }

        private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(MainWindowViewModel.ImagePreviewZoom)
                or nameof(MainWindowViewModel.ImagePreviewRotation)
                or nameof(MainWindowViewModel.ImagePreviewFilePath)
                or nameof(MainWindowViewModel.MediaPreviewFilePath)
                or nameof(MainWindowViewModel.IsPreviewTabSelected)))
            {
                return;
            }

            if (e.PropertyName == nameof(MainWindowViewModel.ImagePreviewFilePath) &&
                sender is MainWindowViewModel vm)
            {
                LoadNaturalImageSize(vm.ImagePreviewFilePath);
            }

            if (e.PropertyName == nameof(MainWindowViewModel.MediaPreviewFilePath) &&
                sender is MainWindowViewModel mediaVm)
            {
                UpdateMediaPreview(mediaVm.MediaPreviewFilePath);
            }

            if (e.PropertyName == nameof(MainWindowViewModel.IsPreviewTabSelected) &&
                sender is MainWindowViewModel tabVm)
            {
                if (tabVm.IsPreviewTabSelected)
                    UpdateMediaPreview(tabVm.MediaPreviewFilePath);
                else
                    StopMediaPreview();
            }

            Dispatcher.UIThread.Post(UpdateImagePreviewLayout, DispatcherPriority.Background);
        }

        private void ImagePreviewScrollViewer_OnSizeChanged(object? sender, SizeChangedEventArgs e)
            => UpdateImagePreviewLayout();

        private void ImagePreviewScrollViewer_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm || !vm.IsImagePreviewAvailable)
                return;

            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
                return;

            if (e.Delta.Y > 0)
                vm.ImagePreviewZoomInCommand.Execute(null);
            else if (e.Delta.Y < 0)
                vm.ImagePreviewZoomOutCommand.Execute(null);

            e.Handled = true;
        }

        private void PanLeft_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => PanPreview(-PreviewPanStep, 0);
        private void PanRight_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => PanPreview(PreviewPanStep, 0);
        private void PanUp_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => PanPreview(0, -PreviewPanStep);
        private void PanDown_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => PanPreview(0, PreviewPanStep);

        private void PanPreview(double dx, double dy)
        {
            if (ImagePreviewScrollViewer is null)
                return;

            var extent = ImagePreviewScrollViewer.Extent;
            var viewport = ImagePreviewScrollViewer.Viewport;
            var maxX = Math.Max(0, extent.Width - viewport.Width);
            var maxY = Math.Max(0, extent.Height - viewport.Height);

            var targetX = Math.Clamp(ImagePreviewScrollViewer.Offset.X + dx, 0, maxX);
            var targetY = Math.Clamp(ImagePreviewScrollViewer.Offset.Y + dy, 0, maxY);
            ImagePreviewScrollViewer.Offset = new Vector(targetX, targetY);
        }

        private void UpdateImagePreviewLayout()
        {
            if (DataContext is not MainWindowViewModel vm ||
                ImagePreviewScrollViewer is null ||
                ImagePreviewCanvas is null ||
                ImagePreviewImage is null ||
                !vm.IsImagePreviewAvailable)
            {
                return;
            }

            var viewport = ImagePreviewScrollViewer.Viewport;
            if (viewport.Width <= 0 || viewport.Height <= 0)
                return;

            if (_imageNaturalWidth <= 0 || _imageNaturalHeight <= 0)
                LoadNaturalImageSize(vm.ImagePreviewFilePath);

            if (_imageNaturalWidth <= 0 || _imageNaturalHeight <= 0)
                return;

            var angle = NormalizeAngle(vm.ImagePreviewRotation);
            var swapped = angle is 90 or 270;
            var fitWidthBase = swapped ? _imageNaturalHeight : _imageNaturalWidth;
            var fitHeightBase = swapped ? _imageNaturalWidth : _imageNaturalHeight;
            var fitScale = Math.Min(viewport.Width / fitWidthBase, viewport.Height / fitHeightBase);
            if (double.IsNaN(fitScale) || double.IsInfinity(fitScale) || fitScale <= 0)
                fitScale = 1.0;

            var userZoom = Math.Clamp(vm.ImagePreviewZoom, 0.1, 8.0);
            var effectiveScale = fitScale * userZoom;
            var renderedWidth = Math.Max(1.0, _imageNaturalWidth * effectiveScale);
            var renderedHeight = Math.Max(1.0, _imageNaturalHeight * effectiveScale);

            var rotatedBoundWidth = swapped ? renderedHeight : renderedWidth;
            var rotatedBoundHeight = swapped ? renderedWidth : renderedHeight;
            var canvasWidth = Math.Max(viewport.Width, rotatedBoundWidth);
            var canvasHeight = Math.Max(viewport.Height, rotatedBoundHeight);

            ImagePreviewCanvas.Width = canvasWidth;
            ImagePreviewCanvas.Height = canvasHeight;
            ImagePreviewImage.Width = renderedWidth;
            ImagePreviewImage.Height = renderedHeight;
            ImagePreviewImage.RenderTransform = BuildRotationTransform(angle, renderedWidth, renderedHeight);

            var centeredLeft = (canvasWidth - rotatedBoundWidth) / 2.0;
            var centeredTop = (canvasHeight - rotatedBoundHeight) / 2.0;
            Canvas.SetLeft(ImagePreviewImage, centeredLeft);
            Canvas.SetTop(ImagePreviewImage, centeredTop);

            var maxX = Math.Max(0, canvasWidth - viewport.Width);
            var maxY = Math.Max(0, canvasHeight - viewport.Height);

            if (userZoom <= 1.0001)
            {
                ImagePreviewScrollViewer.Offset = new Vector(maxX / 2.0, maxY / 2.0);
            }
            else
            {
                var clampedX = Math.Clamp(ImagePreviewScrollViewer.Offset.X, 0, maxX);
                var clampedY = Math.Clamp(ImagePreviewScrollViewer.Offset.Y, 0, maxY);
                ImagePreviewScrollViewer.Offset = new Vector(clampedX, clampedY);
            }
        }

        private static Transform BuildRotationTransform(int angle, double width, double height)
        {
            return angle switch
            {
                90 => CreateTransformGroup(new RotateTransform(90), new TranslateTransform(height, 0)),
                180 => CreateTransformGroup(new RotateTransform(180), new TranslateTransform(width, height)),
                270 => CreateTransformGroup(new RotateTransform(270), new TranslateTransform(0, width)),
                _ => new MatrixTransform(Matrix.Identity)
            };
        }

        private static Transform CreateTransformGroup(params Transform[] transforms)
        {
            var group = new TransformGroup();
            foreach (var transform in transforms)
                group.Children.Add(transform);

            return group;
        }

        private void LoadNaturalImageSize(string? path)
        {
            _imageNaturalWidth = 0;
            _imageNaturalHeight = 0;

            if (string.IsNullOrWhiteSpace(path))
                return;

            lock (ImageNaturalSizeCacheSync)
            {
                if (ImageNaturalSizeCache.TryGetValue(path, out var cached))
                {
                    _imageNaturalWidth = cached.Width;
                    _imageNaturalHeight = cached.Height;
                    return;
                }
            }

            try
            {
                if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                {
                    using var assetStream = AssetLoader.Open(new Uri(path));
                    using var bmp = new Bitmap(assetStream);
                    _imageNaturalWidth = bmp.PixelSize.Width;
                    _imageNaturalHeight = bmp.PixelSize.Height;
                    CacheNaturalImageSize(path, _imageNaturalWidth, _imageNaturalHeight);
                    return;
                }

                if (Uri.TryCreate(path, UriKind.Absolute, out var absUri) && absUri.IsFile)
                {
                    using var stream = File.OpenRead(absUri.LocalPath);
                    using var bmp = new Bitmap(stream);
                    _imageNaturalWidth = bmp.PixelSize.Width;
                    _imageNaturalHeight = bmp.PixelSize.Height;
                    CacheNaturalImageSize(path, _imageNaturalWidth, _imageNaturalHeight);
                    return;
                }

                if (!File.Exists(path))
                    return;

                using var fileStream = File.OpenRead(path);
                using var fileBmp = new Bitmap(fileStream);
                _imageNaturalWidth = fileBmp.PixelSize.Width;
                _imageNaturalHeight = fileBmp.PixelSize.Height;
                CacheNaturalImageSize(path, _imageNaturalWidth, _imageNaturalHeight);
            }
            catch
            {
                _imageNaturalWidth = 0;
                _imageNaturalHeight = 0;
            }
        }

        private static void CacheNaturalImageSize(string path, double width, double height)
        {
            if (width <= 0 || height <= 0)
                return;

            lock (ImageNaturalSizeCacheSync)
            {
                ImageNaturalSizeCache[path] = (width, height);
            }
        }

        private void UpdateMediaPreview(string? mediaPath)
        {
            if (MediaPreviewVideoView is null)
                return;

            if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            {
                StopMediaPreview();
                return;
            }

            try
            {
                EnsureMediaPreviewInitialized();
                if (_mediaPlayer is null || _libVlc is null)
                    return;

                if (string.Equals(_activeMediaPath, mediaPath, StringComparison.OrdinalIgnoreCase))
                    return;

                _activeMediaPath = mediaPath;
                using var media = new LibVlcMedia(_libVlc, mediaPath, FromType.FromPath);
                _mediaPlayer.Play(media);
            }
            catch
            {
                StopMediaPreview();
            }
        }

        private void EnsureMediaPreviewInitialized()
        {
            if (MediaPreviewVideoView is null)
                return;

            if (_mediaPlayer is not null && _libVlc is not null)
            {
                if (MediaPreviewVideoView.MediaPlayer is null)
                    MediaPreviewVideoView.MediaPlayer = _mediaPlayer;
                return;
            }

            EnsureLibVlcInitialized();
            _libVlc = new LibVLC();
            _mediaPlayer = new MediaPlayer(_libVlc);
            MediaPreviewVideoView.MediaPlayer = _mediaPlayer;
        }

        private static void EnsureLibVlcInitialized()
        {
            if (_libVlcInitialized)
                return;

            lock (LibVlcInitSync)
            {
                if (_libVlcInitialized)
                    return;

                LibVLCSharp.Shared.Core.Initialize();
                _libVlcInitialized = true;
            }
        }

        private void StopMediaPreview()
        {
            _activeMediaPath = null;
            try
            {
                _mediaPlayer?.Stop();
            }
            catch
            {
                // Ignore media stop failures from native backend.
            }
        }

        private void DisposeMediaPreview()
        {
            StopMediaPreview();

            if (MediaPreviewVideoView is not null)
                MediaPreviewVideoView.MediaPlayer = null;

            _mediaPlayer?.Dispose();
            _mediaPlayer = null;
            _libVlc?.Dispose();
            _libVlc = null;
        }

        private void MediaPlayPause_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_mediaPlayer is null)
                return;

            if (_mediaPlayer.IsPlaying)
                _mediaPlayer.Pause();
            else
                _mediaPlayer.Play();
        }

        private void MediaStop_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => StopMediaPreview();

        private void MediaMute_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_mediaPlayer is null)
                return;

            _mediaPlayer.Mute = !_mediaPlayer.Mute;
        }

        private static int NormalizeAngle(int angle)
        {
            var normalized = angle % 360;
            if (normalized < 0)
                normalized += 360;

            return normalized;
        }
    }
}
