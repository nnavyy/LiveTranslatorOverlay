using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LiveTranslatorOverlay.Core;

/// <summary>
/// Provides smooth, bounded mouse-wheel scrolling so tall dialogs do not jump aggressively per wheel notch.
/// </summary>
public static class SmoothScrollBehavior
{
    private static readonly Dictionary<ScrollViewer, double> Targets = [];
    private static bool _renderingHooked;
    private static DateTime _lastFrameUtc = DateTime.UtcNow;

    public static double ComputeStep(int wheelDelta) => Math.Clamp(wheelDelta * 0.14, -24.0, 24.0);

    public static void Attach(ScrollViewer scrollViewer)
    {
        ArgumentNullException.ThrowIfNull(scrollViewer);

        void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv || sv.ScrollableHeight <= 0)
                return;

            // Yield to any nested scrollable region (e.g., DataGrid internal scroll or ComboBox popup)
            // when it can still scroll in the requested direction.
            if (e.OriginalSource is DependencyObject src && src != sv && HasScrollableDescendant(src, sv, e.Delta))
                return;

            e.Handled = true;
            if (!Targets.TryGetValue(sv, out double target))
                target = sv.VerticalOffset;

            Targets[sv] = Math.Clamp(target - ComputeStep(e.Delta), 0.0, sv.ScrollableHeight);
            EnsureRenderingHooked();
        }

        void OnUnloaded(object? _, RoutedEventArgs __)
        {
            Targets.Remove(scrollViewer);
            if (Targets.Count == 0 && _renderingHooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _renderingHooked = false;
            }
        }

        scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        scrollViewer.Unloaded += OnUnloaded;
    }

    private static bool HasScrollableDescendant(DependencyObject origin, ScrollViewer root, int delta)
    {
        DependencyObject? curr = origin;
        while (curr != null && curr != root)
        {
            if (curr is ScrollViewer inner && inner != root && inner.ScrollableHeight > 0)
            {
                if (delta > 0 && inner.VerticalOffset > 0.5) return true;
                if (delta < 0 && inner.VerticalOffset < inner.ScrollableHeight - 0.5) return true;
            }
            curr = VisualTreeHelper.GetParent(curr);
        }
        return false;
    }

    private static void EnsureRenderingHooked()
    {
        if (_renderingHooked) return;
        _lastFrameUtc = DateTime.UtcNow;
        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _lastFrameUtc).TotalSeconds, 0.001, 0.05);
        _lastFrameUtc = now;

        if (Targets.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
            return;
        }

        double factor = 1.0 - Math.Exp(-14.0 * dt);
        foreach (var sv in Targets.Keys.ToArray())
        {
            if (!sv.IsLoaded || sv.ScrollableHeight <= 0)
            {
                Targets.Remove(sv);
                continue;
            }

            double desired = Math.Clamp(Targets[sv], 0.0, sv.ScrollableHeight);
            double current = sv.VerticalOffset;
            double diff = desired - current;

            if (Math.Abs(diff) <= 0.35)
            {
                sv.ScrollToVerticalOffset(desired);
                Targets.Remove(sv);
            }
            else
            {
                sv.ScrollToVerticalOffset(current + (diff * factor));
            }
        }

        if (Targets.Count == 0 && _renderingHooked)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderingHooked = false;
        }
    }
}
