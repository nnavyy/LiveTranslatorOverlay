using System.Windows;
using System.Windows.Threading;
using LiveTranslatorOverlay.Core;
using Microsoft.Win32;

namespace LiveTranslatorOverlay;

public partial class MainWindow
{
    private readonly GuestAudioUsage _usageClock = new(AppSettings.DataDirectory);
    private DispatcherTimer? _usageCheckpoint;

    private void AudioUsage_Loaded(object sender, RoutedEventArgs e)
    {
        TxtAudioUsage.Loaded -= AudioUsage_Loaded;
        SystemEvents.PowerModeChanged += UsagePowerModeChanged;
        Closed += (_, _) => SystemEvents.PowerModeChanged -= UsagePowerModeChanged;
        ShowAudioUsage(_usageClock.Checkpoint());
    }

    private void StartUsageClock(AudioCaptureService capture)
    {
        capture.CaptureEnded += (_, _) => StopWhenCaptureEnds(capture);
        if (!capture.IsCapturing) { StopWhenCaptureEnds(capture); return; }
        _usageClock.Start();
        if (_usageCheckpoint == null)
        {
            _usageCheckpoint = new DispatcherTimer { Interval = GuestAudioUsage.CheckpointInterval };
            _usageCheckpoint.Tick += (_, _) => ShowAudioUsage(_usageClock.Checkpoint());
        }
        _usageCheckpoint.Start();
    }

    private void StopWhenCaptureEnds(AudioCaptureService capture) => Dispatcher.BeginInvoke(new Action(async () =>
    {
        if (_isAudioMode && ReferenceEquals(_audioCaptureService, capture)) await StopAudioModeAsync();
    }));

    private void UsagePowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Suspend) return;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (_isAudioMode) await StopAudioModeAsync();
        }));
    }

    private void StopUsageClock()
    {
        _usageCheckpoint?.Stop();
        ShowAudioUsage(_usageClock.Stop());
    }

    private void ShowAudioUsage(TimeSpan duration)
    {
        if (TxtAudioUsage != null) TxtAudioUsage.Text = $"Audio translation on this device: {GuestAudioUsage.Format(duration)}";
    }
}
