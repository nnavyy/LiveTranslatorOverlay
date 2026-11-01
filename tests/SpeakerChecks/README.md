# Speaker and popup regression checks

Run `dotnet run --project tests/SpeakerChecks/SpeakerChecks.csproj -c Release` on Windows with .NET 10.

Synthetic audio and local Deepgram JSON fixtures verify the targeted label/turn logic. The WPF test creates a non-activating, off-screen window and raises actual wheel events to verify distance, direction, and the accelerated-input cap. It does not open the running translator or capture user audio.

These are regression checks, not a real-voice diarization benchmark. See [speaker labels](../../docs/speaker-labels.md) for the distinction between local estimates and provider word-level attribution.
