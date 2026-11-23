using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiveTranslatorOverlay.Core;

public static class AppBranding
{
    private const string LogoPackUri = "pack://application:,,,/LiveTranslatorOverlay;component/Assets/app-logo.png";
    private const string IconPackUri = "pack://application:,,,/LiveTranslatorOverlay;component/Assets/app-icon.ico";

    public static BitmapImage? LoadWindowIcon() => LoadPackImage(IconPackUri, "app-icon.ico");

    public static BitmapImage? LoadLogoImage() => LoadPackImage(LogoPackUri, "app-logo.png");

    public static Image? CreateLogoControl(double size)
    {
        var source = LoadLogoImage();
        if (source == null) return null;

        var image = new Image
        {
            Source = source,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static BitmapImage? LoadPackImage(string packUri, string fallbackFile)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(packUri, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            try
            {
                string diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", fallbackFile);
                if (!File.Exists(diskPath)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(diskPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
