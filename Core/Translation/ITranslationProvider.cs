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

        /// <summary>
        /// Menerjemahkan teks secara streaming (token per token)
        /// </summary>
        /// <param name="text">Teks asli</param>
        /// <param name="targetLanguage">Kode bahasa target</param>
        /// <param name="sourceLanguage">Kode bahasa sumber</param>
        /// <param name="onTokenReceived">Callback yang dipanggil setiap kali ada potongan teks terjemahan baru</param>
        Task TranslateStreamAsync(string text, string targetLanguage, string sourceLanguage, System.Action<string> onTokenReceived);
    }
}
