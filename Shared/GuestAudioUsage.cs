using System.Globalization;

namespace LiveTranslatorOverlay.Core;

// Call on the UI thread. Counts only the intervals between capture-ready and capture-ended.
public sealed class GuestAudioUsage(string dataDirectory, TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan CheckpointInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(20);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly string _path = Path.Combine(dataDirectory, "guest-audio-usage.txt");
    private long _lastTimestamp;
    private TimeSpan? _total;
    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;
        _lastTimestamp = _time.GetTimestamp();
        IsRunning = true;
    }

    public TimeSpan Checkpoint()
    {
        _total ??= Read();
        if (!IsRunning) return _total.Value;
        long now = _time.GetTimestamp();
        var elapsed = _time.GetElapsedTime(_lastTimestamp, now);
        _lastTimestamp = now;
        // A missed heartbeat after sleep or a long UI stall must not award the gap.
        if (elapsed > TimeSpan.Zero && elapsed <= MaximumGap && _total.Value <= TimeSpan.MaxValue - elapsed)
        {
            _total += elapsed;
            Save(_total.Value);
        }
        return _total.Value;
    }

    public TimeSpan Stop()
    {
        var total = Checkpoint();
        IsRunning = false;
        return total;
    }

    public static string Format(TimeSpan duration) => $"{(long)duration.TotalHours:N0} h {duration.Minutes:D2} m";

    private TimeSpan Read()
    {
        try
        {
            string text = File.ReadAllText(_path);
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                ? TimeSpan.FromTicks(ticks) : TimeSpan.Zero;
        }
        catch (IOException) { return TimeSpan.Zero; }
        catch (UnauthorizedAccessException) { return TimeSpan.Zero; }
    }

    private void Save(TimeSpan total)
    {
        string temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(temporary, total.Ticks.ToString(CultureInfo.InvariantCulture));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
