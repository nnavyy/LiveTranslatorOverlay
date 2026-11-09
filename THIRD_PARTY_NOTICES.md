# Third-party notices for local speech

The app includes Whisper.net 1.9.1 and its native CPU and NoAvx runtimes for Whisper speech and Silero VAD (MIT); their copyright/license notices accompany the package assets. Other existing dependencies retain their original notices and licenses.

Version 2.1.1 bundles the .NET runtime and Microsoft Visual C++ redistributable DLLs for application-local use. Their upstream redistribution terms apply. Whisper base/small models (OpenAI, MIT) and Silero VAD (MIT) are downloaded from Hugging Face on first use; model checksums are verified before loading. See licenses/Whisper-models.txt and licenses/Silero-VAD.txt.

Optional locally prepared recognition assets are not bundled into the application ZIP:

- Distil-Whisper large-v3: MIT, https://huggingface.co/distil-whisper/distil-large-v3 .
- GigaAM v2 RNNT and Python code: MIT, copyright 2024 GigaChat Team, https://github.com/salute-developers/GigaAM/blob/7447938d791c4f3e643386ee22c33777004293a5/LICENSE .
- Silero VAD: MIT, https://github.com/snakers4/silero-vad/blob/master/LICENSE .
- PyTorch, Transformers, NumPy, SoundFile and other optional environment dependencies retain their respective licenses. FFmpeg's installed build has its own LGPL/GPL distribution terms; no FFmpeg executable is bundled here.

Retain upstream license texts when redistributing model/environment assets. Review the full dependency inventory before selling a bundled standalone runtime; the current release uses separately prepared local assets. The existing application source through the public revision remains covered by its MIT license (LICENSE).

Optional local translation: M2M100 418M (MIT), CTranslate2 int8 conversion auralmira/m2m100-418m-ct2-int8 revision e205afefce2fd6933a1dca3b92b5063894f2f2bb. Publisher did not specify the upstream base revision. ASR WER is not a translation metric.
