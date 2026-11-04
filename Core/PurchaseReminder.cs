using System.Windows;
using System.Windows.Controls;

namespace LiveTranslatorOverlay.Core;

public static class PurchaseReminder
{
    public const string Price = "US$13";
    public const bool PurchaseRequiredToContinue = false;

    public static void Show(Window owner)
    {
        var window = new Window
        {
            Owner = owner, Title = "Lifetime license", Width = 420, Height = 245,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "Support Live Translator Overlay",
            FontSize = 19, Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"A lifetime license costs {Price}, paid once.\nYou can choose Not now and keep using the app.\n\nPurchasing is not configured yet.",
            TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(new Button
        {
            Content = $"Buy lifetime — {Price}", IsEnabled = false,
            Padding = new Thickness(12, 6, 12, 6), ToolTip = "Checkout has not been configured."
        });
        var later = new Button { Content = "Not now", IsCancel = true, IsDefault = true, Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(12, 6, 12, 6) };
        later.Click += (_, _) => window.Close();
        buttons.Children.Add(later);
        panel.Children.Add(buttons);
        window.Content = panel;
        window.ShowDialog();
    }
}
