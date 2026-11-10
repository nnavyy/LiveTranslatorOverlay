using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using LiveTranslatorOverlay.Core.Translation;

internal static class RussianTranslationProbe
{
    public static async Task RunAsync(bool forceFallback)
    {
        // These authored phrases contain no captured audio, user transcripts, or credentials.
        var cases = new (string Id, string Source, string Expected)[]
        {
            ("negated-command", "Не открывайте дверь.", "Do not open the door."),
            ("negated-agreement", "Я не согласен.", "I disagree."),
            ("number-contrast", "Цена 900 долларов, а не 100.", "The price is 900 dollars, not 100."),
            ("time-contrast", "Встреча в 15:30, а не в 17:30.", "The meeting is at 15:30, not 17:30."),
            ("names", "Маша встретила Юрия в Казани.", "Masha met Yuri in Kazan."),
            ("food-context", "Добавь лук в суп.", "Add onion to the soup."),
            ("game-context", "Возьми лук и стреляй в цель.", "Take the bow and shoot at the target."),
            ("idiom", "У меня руки не доходят до этого.", "I haven't gotten around to it."),
        };
        var provider = new GoogleFreeTranslateProvider();
        var field = typeof(GoogleFreeTranslateProvider).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!;
        using var original = (HttpClient)field.GetValue(provider)!;
        var handler = new AttemptsHandler(forceFallback);
        using var observed = new HttpClient(handler) { Timeout = original.Timeout };
        foreach (var header in original.DefaultRequestHeaders) observed.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        field.SetValue(provider, observed);
        foreach (var item in cases)
        {
            handler.Attempts.Clear();
            var timer = Stopwatch.StartNew();
            string? output = null, error = null;
            try { output = await provider.TranslateAsync(item.Source, "en", "ru"); }
            catch (HttpRequestException ex) { error = ex.Message; }
            Console.WriteLine(JsonSerializer.Serialize(new { item.Id, item.Source, item.Expected, output, error,
                elapsedSeconds = timer.Elapsed.TotalSeconds, attempts = handler.Attempts.ToArray() }));
        }
    }

    private sealed class AttemptsHandler : DelegatingHandler
    {
        private readonly bool _forceFallback;
        public List<object> Attempts { get; } = [];
        public AttemptsHandler(bool forceFallback) : base(new HttpClientHandler()) => _forceFallback = forceFallback;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var timer = Stopwatch.StartNew();
            string host = request.RequestUri!.Host;
            if (_forceFallback && host == "translate.googleapis.com")
            {
                Attempts.Add(new { host, http = 429, simulated = true, seconds = 0 });
                return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            }
            try
            {
                var response = await base.SendAsync(request, token);
                Attempts.Add(new { host, http = (int)response.StatusCode, seconds = timer.Elapsed.TotalSeconds });
                return response;
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
            {
                Attempts.Add(new { host, failure = error.GetType().Name, seconds = timer.Elapsed.TotalSeconds });
                throw;
            }
        }
    }
}
