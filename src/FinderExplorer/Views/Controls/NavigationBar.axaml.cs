using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace FinderExplorer.Views.Controls
{
    public partial class NavigationBar : UserControl
    {
        public NavigationBar()
        {
            InitializeComponent();
            AttachedToVisualTree += OnAttachedToVisualTree;
            DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is TopLevel topLevel)
                topLevel.KeyDown += OnTopLevelKeyDown;
        }

        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is TopLevel topLevel)
                topLevel.KeyDown -= OnTopLevelKeyDown;
        }

        private void OnTopLevelKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.F || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
                return;

            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }
}
