using System;
using System.Windows.Controls;
using System.Windows.Input;

namespace LiveTranslatorOverlay.Core;

public static class ReleaseNotesScroll
{
    public static void Attach(TextBox text)
    {
        text.PreviewMouseWheel += (_, e) =>
        {
            // One wheel notch moves 12 DIPs; keep accelerated wheels bounded.
            double step = Math.Clamp(e.Delta * 0.1, -24, 24);
            text.ScrollToVerticalOffset(Math.Max(0, text.VerticalOffset - step));
            e.Handled = true;
        };
    }
}
