using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace LiveTranslatorOverlay.Core
{
    public class ScreenOcrService
    {
        private OcrEngine? _ocrEngine;
        public string? LastError { get; private set; }
        public bool IsRequestedLanguageSupported { get; private set; } = true;
        public string CurrentLanguageTag { get; private set; } = "auto";

        public ScreenOcrService()
        {
            InitializeOcrEngine("auto");
        }

        public void ChangeLanguage(string languageTag)
        {
            if (CurrentLanguageTag == languageTag && _ocrEngine != null) return;
            InitializeOcrEngine(languageTag);
            CurrentLanguageTag = languageTag;
        }

        private void InitializeOcrEngine(string languageTag)
        {
            _ocrEngine = null;
            IsRequestedLanguageSupported = true;

            if (languageTag != "auto")
            {
                var lang = new Language(languageTag);
                if (OcrEngine.IsLanguageSupported(lang))
                {
                    _ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
                }
                else
                {
                    IsRequestedLanguageSupported = false;
                    LastError = $"Paket OCR Windows '{languageTag}' belum terpasang di PC. Buka Settings -> Time & Language -> Language.";
                }
            }

            // Fallback if auto or requested language not installed
            if (_ocrEngine == null)
            {
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_ocrEngine == null)
                {
                    var enLang = new Language("en-US");
                    if (OcrEngine.IsLanguageSupported(enLang))
                    {
                        _ocrEngine = OcrEngine.TryCreateFromLanguage(enLang);
                    }
                }
            }

            if (_ocrEngine == null)
            {
                LastError = "OCR Engine tidak tersedia. Pastikan language pack Windows sudah terinstall.";
            }
            else if (IsRequestedLanguageSupported)
            {
                LastError = null;
            }
        }

        /// <summary>
        /// Recognize text from a screen region. 
        /// screenRect must already be in physical screen pixels (not WPF DIPs).
        /// </summary>
        public async Task<string> RecognizeRegionAsync(int screenX, int screenY, int width, int height)
        {
            if (_ocrEngine == null)
                return $"[Error: {LastError}]";

            if (width <= 0 || height <= 0)
                return string.Empty;

            try
            {
                // Capture screen region using GDI
                using var rawBitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(rawBitmap))
                {
                    g.CopyFromScreen(screenX, screenY, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                }

                // Pass 1: Preprocessed bitmap (upscaled + inverted if dark background)
                using var preprocessedBitmap = PreprocessImage(rawBitmap);
                string text = await RecognizeBitmapAsync(preprocessedBitmap);

                // Pass 2: Fallback to raw bitmap if preprocessed yielded nothing
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = await RecognizeBitmapAsync(rawBitmap);
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    LastError = null;
                    return text.Trim();
                }

                // If text is still empty and requested language is not supported, inform the user
                if (!IsRequestedLanguageSupported)
                {
                    return $"[OCR: Language pack '{CurrentLanguageTag}' not installed in Windows]";
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return $"[OCR Error: {ex.Message}]";
            }
        }

        private async Task<string> RecognizeBitmapAsync(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Bmp);
            ms.Position = 0;

            var ras = ms.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

            var ocrResult = await _ocrEngine!.RecognizeAsync(softwareBitmap);
            return ocrResult?.Text ?? string.Empty;
        }

        private Bitmap PreprocessImage(Bitmap src)
        {
            double edgeLuminance = GetAverageEdgeLuminance(src);
            bool isDarkBackground = edgeLuminance < 128.0;

            // Smart upscale: 3x for very small text, 2x for medium, 1x for large
            int scale;
            if (src.Height < 50 || src.Width < 150)
                scale = 3; // Very small text regions need aggressive upscaling
            else if (src.Height < 90 || src.Width < 250)
                scale = 2; // Medium regions benefit from moderate upscaling
            else
                scale = 1;

            int newW = src.Width * scale;
            int newH = src.Height * scale;

            var dst = new Bitmap(newW, newH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                if (isDarkBackground)
                {
                    // Invert colors using ColorMatrix (makes white text on dark background -> dark text on white)
                    // Also convert to grayscale in the same pass for better OCR
                    var colorMatrix = new ColorMatrix(new float[][]
                    {
                        new float[] {-0.299f, -0.299f, -0.299f, 0, 0},
                        new float[] {-0.587f, -0.587f, -0.587f, 0, 0},
                        new float[] {-0.114f, -0.114f, -0.114f, 0, 0},
                        new float[] { 0,       0,       0,      1, 0},
                        new float[] { 1,       1,       1,      0, 1}
                    });

                    using var imageAttributes = new ImageAttributes();
                    imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                    g.DrawImage(src, new Rectangle(0, 0, newW, newH), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, imageAttributes);
                }
                else
                {
                    // Convert to grayscale to remove color noise that confuses OCR
                    var grayMatrix = new ColorMatrix(new float[][]
                    {
                        new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                        new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                        new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                        new float[] {0,      0,      0,      1, 0},
                        new float[] {0,      0,      0,      0, 1}
                    });

                    using var imageAttributes = new ImageAttributes();
                    imageAttributes.SetColorMatrix(grayMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                    g.DrawImage(src, new Rectangle(0, 0, newW, newH), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, imageAttributes);
                }
            }

            // Apply unsharp mask (sharpen) to boost character stroke edges — improves OCR on blurry/small text
            if (scale >= 2)
            {
                ApplySharpen(dst);
            }

            return dst;
        }

        /// <summary>
        /// Simple 3x3 unsharp mask sharpening kernel to boost edges for OCR.
        /// Uses a center-weighted kernel: [0 -1 0 / -1 5 -1 / 0 -1 0]
        /// </summary>
        private void ApplySharpen(Bitmap bmp)
        {
            try
            {
                int w = bmp.Width, h = bmp.Height;
                var lockBits = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                int stride = lockBits.Stride;
                byte[] pixels = new byte[stride * h];
                byte[] output = new byte[stride * h];
                System.Runtime.InteropServices.Marshal.Copy(lockBits.Scan0, pixels, 0, pixels.Length);
                Array.Copy(pixels, output, pixels.Length);

                for (int y = 1; y < h - 1; y++)
                {
                    for (int x = 1; x < w - 1; x++)
                    {
                        for (int c = 0; c < 3; c++) // B, G, R channels
                        {
                            int idx = y * stride + x * 4 + c;
                            int val = pixels[idx] * 5
                                    - pixels[(y - 1) * stride + x * 4 + c]
                                    - pixels[(y + 1) * stride + x * 4 + c]
                                    - pixels[y * stride + (x - 1) * 4 + c]
                                    - pixels[y * stride + (x + 1) * 4 + c];
                            output[idx] = (byte)Math.Clamp(val, 0, 255);
                        }
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(output, 0, lockBits.Scan0, output.Length);
                bmp.UnlockBits(lockBits);
            }
            catch
            {
                // If sharpening fails, continue with unsharpened bitmap — OCR will still work
            }
        }

        private double GetAverageEdgeLuminance(Bitmap bmp)
        {
            try
            {
                long totalLum = 0;
                int count = 0;
                int stepX = Math.Max(1, bmp.Width / 15);
                int stepY = Math.Max(1, bmp.Height / 10);

                // Sample top and bottom borders
                for (int x = 0; x < bmp.Width; x += stepX)
                {
                    var c1 = bmp.GetPixel(x, 0);
                    var c2 = bmp.GetPixel(x, bmp.Height - 1);
                    totalLum += (long)(0.299 * c1.R + 0.587 * c1.G + 0.114 * c1.B);
                    totalLum += (long)(0.299 * c2.R + 0.587 * c2.G + 0.114 * c2.B);
                    count += 2;
                }

                // Sample left and right borders
                for (int y = 0; y < bmp.Height; y += stepY)
                {
                    var c1 = bmp.GetPixel(0, y);
                    var c2 = bmp.GetPixel(bmp.Width - 1, y);
                    totalLum += (long)(0.299 * c1.R + 0.587 * c1.G + 0.114 * c1.B);
                    totalLum += (long)(0.299 * c2.R + 0.587 * c2.G + 0.114 * c2.B);
                    count += 2;
                }

                return count > 0 ? (double)totalLum / count : 255.0;
            }
            catch
            {
                return 255.0;
            }
        }
    }
}
