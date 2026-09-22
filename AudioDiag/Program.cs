using System;
using System.Threading;
using System.Threading.Tasks;
using LiveTranslatorOverlay.Core;
using NAudio.CoreAudioApi;

class Program {
    static async Task Main() {
        var capture = new AudioCaptureService();
        int chunkCount = 0;
        capture.AudioChunkReady += (s, chunk) => {
            chunkCount++;
            float energy = 0;
            foreach (var f in chunk) energy += Math.Abs(f);
            Console.WriteLine($"CHUNK {chunkCount}: {chunk.Length} samples, Avg Energy: {energy/chunk.Length:F5}");
        };
        
        var enumerator = new MMDeviceEnumerator();
        var defaultRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        
        await capture.StartAsync(defaultRender.ID, -1);
        Console.WriteLine("Capturing for 10 seconds...");
        await Task.Delay(10000);
        capture.Stop();
        Console.WriteLine($"Done. Captured {chunkCount} chunks.");
    }
}
