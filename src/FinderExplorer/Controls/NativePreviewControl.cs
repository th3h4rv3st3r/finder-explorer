// Copyright (c) Finder Explorer. All rights reserved.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using FinderExplorer.Native.Bridge;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FinderExplorer.Controls;

/// <summary>
/// Avalonia control that hosts a native Windows HWND used by IPreviewHandler.
/// </summary>
public sealed class NativePreviewControl : NativeControlHost
{
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<NativePreviewControl, string?>(nameof(FilePath));

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    private IntPtr _previewContext = IntPtr.Zero;
    private IPlatformHandle? _hwndHandle;
    private bool _isNativeBridgeUnavailable;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == FilePathProperty)
        {
            LoadPreview();
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        _hwndHandle = handle;
        LoadPreview();
        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        UnloadPreview();
        base.DestroyNativeControlCore(control);
    }

    private void LoadPreview()
    {
        UnloadPreview();

        if (_isNativeBridgeUnavailable || string.IsNullOrEmpty(FilePath) || _hwndHandle == null || !File.Exists(FilePath))
            return;

        try
        {
            // Pass current bounds to native preview host.
            var rc = new NativeBridge.RECT
            {
                left = 0,
                top = 0,
                right = (int)Bounds.Width,
                bottom = (int)Bounds.Height
            };

            _previewContext = NativeBridge.Preview_Create(FilePath, _hwndHandle.Handle, ref rc);
        }
        catch (DllNotFoundException)
        {
            _isNativeBridgeUnavailable = true;
            _previewContext = IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            _isNativeBridgeUnavailable = true;
            _previewContext = IntPtr.Zero;
        }
        catch
        {
            _previewContext = IntPtr.Zero;
        }
    }

    private void UnloadPreview()
    {
        if (_previewContext != IntPtr.Zero)
        {
            try
            {
                NativeBridge.Preview_Destroy(_previewContext);
            }
            catch
            {
                // Ignore teardown failures; host will recreate on next valid selection.
            }
            _previewContext = IntPtr.Zero;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_previewContext != IntPtr.Zero)
        {
            try
            {
                var rc = new NativeBridge.RECT
                {
                    left = 0,
                    top = 0,
                    right = (int)e.NewSize.Width,
                    bottom = (int)e.NewSize.Height
                };
                NativeBridge.Preview_Resize(_previewContext, ref rc);
            }
            catch
            {
                // Ignore transient resize failures from native preview handlers.
            }
        }
    }
}
