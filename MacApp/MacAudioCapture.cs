using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace LiveTranslatorMac;

internal sealed class MacAudioCapture : IAsyncDisposable
{
    private const int ChunkSize = 16_000 * 4;
    private static readonly SamplesCallback Samples = OnSamples;
    private static readonly StatusCallback Status = OnStatus;
    private static MacAudioCapture? _active;
    private static long _nextId;

    private readonly long _id = Interlocked.Increment(ref _nextId);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<float[]> _chunks = Channel.CreateBounded<float[]>(new BoundedChannelOptions(2)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly object _gate = new();
    private float[] _buffer = new float[ChunkSize];
    private int _buffered;
    private bool _started;

    public ChannelReader<float[]> Chunks => _chunks.Reader;
    public Task Stopped => _stopped.Task;
    public string? Error { get; private set; }

    public async Task StartAsync(bool microphone, CancellationToken token)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Audio capture needs macOS.");
        if (Interlocked.CompareExchange(ref _active, this, null) != null)
            throw new InvalidOperationException("Audio capture is already running.");
        try
        {
            NativeStart(_id, microphone ? 1 : 0, Samples, Status);
            _started = true;
        }
        catch
        {
            Interlocked.CompareExchange(ref _active, null, this);
            throw;
        }
        await _ready.Task.WaitAsync(token);
    }

    public async ValueTask DisposeAsync()
    {
        if (_started && !_stopped.Task.IsCompleted)
        {
            NativeStop(_id);
            await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        _chunks.Writer.TryComplete();
    }

    private static void OnSamples(long id, IntPtr data, int frames)
    {
        var capture = Volatile.Read(ref _active);
        if (capture == null || capture._id != id || frames <= 0 || frames > ChunkSize || data == IntPtr.Zero) return;
        lock (capture._gate)
        {
            for (int offset = 0; offset < frames;)
            {
                int count = Math.Min(frames - offset, ChunkSize - capture._buffered);
                Marshal.Copy(IntPtr.Add(data, offset * sizeof(float)), capture._buffer, capture._buffered, count);
                capture._buffered += count;
                offset += count;
                if (capture._buffered != ChunkSize) continue;
                capture._chunks.Writer.TryWrite(capture._buffer);
                capture._buffer = new float[ChunkSize];
                capture._buffered = 0;
            }
        }
    }

    private static void OnStatus(long id, int code, IntPtr message)
    {
        var capture = Volatile.Read(ref _active);
        if (capture == null || capture._id != id) return;
        if (code == 1)
        {
            capture._ready.TrySetResult();
            return;
        }
        capture.Error = code < 0 ? Marshal.PtrToStringUTF8(message) ?? "Audio capture failed." : null;
        if (capture.Error is { } error) capture._ready.TrySetException(new InvalidOperationException(error));
        else capture._ready.TrySetCanceled();
        Interlocked.CompareExchange(ref _active, null, capture);
        capture._stopped.TrySetResult();
        capture._chunks.Writer.TryComplete();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SamplesCallback(long id, IntPtr data, int frames);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void StatusCallback(long id, int code, IntPtr message);
    [DllImport("libLiveTranslatorAudio.dylib", EntryPoint = "lt_start_audio", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeStart(long id, int microphone, SamplesCallback samples, StatusCallback status);
    [DllImport("libLiveTranslatorAudio.dylib", EntryPoint = "lt_stop_audio", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeStop(long id);
}
