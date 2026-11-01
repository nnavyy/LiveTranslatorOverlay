using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.Json;
using LiveTranslatorOverlay.Core;

int passed = 0, total = 0;
void Check(string title, bool valid, string detail)
{
    total++;
    if (valid) passed++;
    Console.WriteLine($"{(valid ? "PASS" : "FAIL")}: {title}: {detail}");
}
static float[] Voice(double hz)
{
    return Enumerable.Range(0, 16000).Select(i => (float)(0.12 * Math.Sin(2 * Math.PI * hz * i / 16000))).ToArray();
}

AcousticSpeakerDetector.Reset();
var low = AcousticSpeakerDetector.DetectSpeaker(Voice(150));
var high = AcousticSpeakerDetector.DetectSpeaker(Voice(160));
Check("Small pitch changes do not invent a different person", low.Label == high.Label, $"150 Hz={low.Label}; 160 Hz={high.Label}");
var silent = AcousticSpeakerDetector.DetectSpeaker(new float[16000]);
Check("Unvoiced input does not inherit a known person's label", silent.Label == "Unknown speaker", silent.Label);
Check("Local estimates do not claim gender", low.Gender == "Unknown" && high.Gender == "Unknown", low.Gender + "/" + high.Gender);
AcousticSpeakerDetector.Reset();
var a = AcousticSpeakerDetector.DetectSpeaker(Voice(120));
var b = AcousticSpeakerDetector.DetectSpeaker(Voice(240));
var again = AcousticSpeakerDetector.DetectSpeaker(Voice(120));
Check("Harmonic peaks do not merge distinct pitch groups", a.Label != b.Label && again.Label == a.Label,
    $"120Hz={a.Label}; 240Hz={b.Label}; returning={again.Label}");
var mixed = AcousticSpeakerDetector.DetectSpeaker(Voice(150).Concat(Voice(280)).ToArray());
Check("Mixed-pitch chunks are not assigned wholesale to one person", mixed.Label == "Unknown speaker", mixed.Label);
AcousticSpeakerDetector.Reset();
var cancelled = new CancellationToken(true);
var obsolete = AcousticSpeakerDetector.DetectSpeaker(Voice(180), cancelled);
var fresh = AcousticSpeakerDetector.DetectSpeaker(Voice(180));
Check("Cancelled sessions cannot register a speaker", obsolete.SpeakerIndex == -1 && fresh.SpeakerIndex == 0,
    $"obsolete={obsolete.SpeakerIndex}; new={fresh.SpeakerIndex}");
var dc = AcousticSpeakerDetector.DetectSpeaker(Enumerable.Repeat(0.1f, 16000).ToArray());
Check("Constant input is not treated as a voice", dc.SpeakerIndex == -1, dc.Label);

var service = new DiarizedSttService("dummy-test-key");
var turns = new List<SpeakerTranscriptEventArgs>();
service.TranscriptReady += (_, e) => turns.Add(e);
var parse = typeof(DiarizedSttService).GetMethod("ParseAndEmit", BindingFlags.Instance | BindingFlags.NonPublic)!;
parse.Invoke(service, ["{\"type\":\"Results\",\"is_final\":true,\"channel\":{\"alternatives\":[{\"transcript\":\"hello there\",\"words\":[{\"word\":\"hello there\",\"confidence\":0.98,\"start\":0,\"end\":1}]}]}}"]);
Check("Missing diarization is unknown, not Speaker 1", turns.Count == 1 && turns[0].SpeakerIndex == -1,
    $"emitted speaker={turns.FirstOrDefault()?.SpeakerIndex}");
turns.Clear();
var words = new[] { new { word = "alpha", speaker = 0, confidence = 0.99, start = 0.0, end = 0.4 },
    new { word = "beta", speaker = 1, confidence = 0.99, start = 0.4, end = 0.8 },
    new { word = "again", speaker = 0, confidence = 0.99, start = 0.8, end = 1.2 } };
parse.Invoke(service, [JsonSerializer.Serialize(new { type = "Results", is_final = true,
    channel = new { alternatives = new[] { new { transcript = "alpha beta again", words } } } })]);
Check("Final word-level speaker turns retain order", turns.Select(t => t.SpeakerIndex).SequenceEqual(new[] { 0, 1, 0 })
    && turns.Select(t => t.Text).SequenceEqual(new[] { "alpha", "beta", "again" }), string.Join(" / ", turns.Select(t => $"{t.SpeakerIndex}:{t.Text}")));
turns.Clear();
parse.Invoke(service, ["{\"type\":\"Results\",\"is_final\":true,\"channel\":{\"alternatives\":[{\"transcript\":\"hello hello\",\"words\":[{\"word\":\"hello\",\"speaker\":0,\"confidence\":0.99,\"start\":0,\"end\":0.4},{\"word\":\"hello\",\"speaker\":1,\"confidence\":0.99,\"start\":0.4,\"end\":0.8}]}]}}"]);
Check("Identical words from different speakers remain separate", turns.Count == 2 && turns[0].SpeakerIndex == 0 && turns[1].SpeakerIndex == 1,
    string.Join(" / ", turns.Select(t => $"{t.SpeakerIndex}:{t.Text}")));
var closing = new DiarizedSttService("dummy");
typeof(DiarizedSttService).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(closing, new CancellationTokenSource());
bool disposedTwice = true;
try { await closing.DisposeAsync(); await closing.DisposeAsync(); } catch (ObjectDisposedException) { disposedTwice = false; }
Check("Stop during connection can safely clean up twice", disposedTwice, $"idempotent={disposedTwice}");

double movement = double.PositiveInfinity;
double upward = double.PositiveInfinity, accelerated = double.PositiveInfinity;
Exception? uiError = null;
var ui = new Thread(() =>
{
    try
    {
        var text = new TextBox { Text = string.Join("\n", Enumerable.Range(0, 1000)), Width = 300, Height = 200 };
        var window = new Window { Content = text, Width = 320, Height = 240, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
        var behavior = typeof(AcousticSpeakerDetector).Assembly.GetType("LiveTranslatorOverlay.Core.ReleaseNotesScroll");
        behavior?.GetMethod("Attach")?.Invoke(null, [text]);
        window.Show();
        text.ApplyTemplate(); text.UpdateLayout();
        text.ScrollToVerticalOffset(1000);
        text.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        double before = text.VerticalOffset;
        text.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        text.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        movement = text.VerticalOffset - before;
        before = text.VerticalOffset;
        text.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        text.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        upward = text.VerticalOffset - before;
        before = text.VerticalOffset;
        text.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -1200) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
        text.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        accelerated = text.VerticalOffset - before;
        window.Close();
    }
    catch (Exception ex) { uiError = ex; }
});
ui.SetApartmentState(ApartmentState.STA); ui.Start(); ui.Join();
Check("Release-note wheel movement is slow and bounded", uiError == null && movement > 0 && movement <= 24, $"one notch moved {movement}px; {uiError?.Message}");
Check("Scroll reverses and caps accelerated wheel events", upward == -12 && accelerated == 24, $"up={upward}px; accelerated={accelerated}px");
Console.WriteLine($"Speaker/scroll checks: {passed}/{total}");
Environment.ExitCode = passed == total ? 0 : 1;
