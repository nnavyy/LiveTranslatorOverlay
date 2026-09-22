using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace LiveTranslatorOverlay.Core
{
    public class ScreenOcrService
    {
        private OcrEngine? _ocrEngine;
        public string? LastError { get; private set; }

        private string _currentLanguage = "auto";

        public ScreenOcrService()
        {
            InitializeOcrEngine("auto");
        }

        public void ChangeLanguage(string languageTag)
        {
            if (_currentLanguage == languageTag) return;
            InitializeOcrEngine(languageTag);
            _currentLanguage = languageTag;
        }

        private void InitializeOcrEngine(string languageTag)
        {
            _ocrEngine = null;

            if (languageTag != "auto")
            {
                var lang = new Windows.Globalization.Language(languageTag);
                if (OcrEngine.IsLanguageSupported(lang))
                {
                    _ocrEngine = OcrEngine.TryCreateFromLanguage(lang);
                }
            }

            // Fallbacks if specific lang failed or auto
            if (_ocrEngine == null)
            {
                _ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (_ocrEngine == null)
                {
                    var enLang = new Windows.Globalization.Language("en-US");
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
            else
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
                using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(screenX, screenY, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                }

                // Convert System.Drawing.Bitmap to WinRT SoftwareBitmap via MemoryStream
                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Bmp);
                ms.Position = 0;

                // Create IRandomAccessStream from MemoryStream
                var ras = ms.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(ras);
                var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

                var ocrResult = await _ocrEngine!.RecognizeAsync(softwareBitmap);
                LastError = null;
                return ocrResult.Text;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return $"[OCR Error: {ex.Message}]";
            }
        }
    }
}
