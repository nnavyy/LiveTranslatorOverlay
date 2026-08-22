using System.Threading.Tasks;

namespace LiveTranslatorOverlay.Core.Translation
{
    public interface ITranslationProvider
    {
        /// <summary>
        /// Menerjemahkan teks
        /// </summary>
        /// <param name="text">Teks asli</param>
        /// <param name="targetLanguage">Kode bahasa target (misal: "id", "en")</param>
        /// <param name="sourceLanguage">Kode bahasa sumber ("auto" untuk deteksi otomatis)</param>
        /// <returns>Hasil terjemahan</returns>
        Task<string> TranslateAsync(string text, string targetLanguage = "id", string sourceLanguage = "auto");
    }
}
