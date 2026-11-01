using LiveTranslatorOverlay.Core.Translation;

// Optional live check. Only these synthetic phrases are sent; no credentials or recordings.
var cases = new (string Source, string From, string To, Func<string, bool> Check)[]
{
    ("Do not open the door.", "en", "id", s => (s.Contains("jangan") || s.Contains("tidak")) && s.Contains("pintu")),
    ("The price is 900 dollars, not 100 dollars.", "en", "id", s => s.Contains("900") && s.Contains("100") && (s.Contains("bukan") || s.Contains("tidak"))),
    ("I will arrive on November 1.", "en", "id", s => s.Contains("november") && s.Contains('1')),
    ("Saya tidak setuju.", "id", "en", s => s.Contains("not") || s.Contains("disagree")),
    ("Matikan mikrofon saya.", "id", "en", s => s.Contains("microphone") && (s.Contains("off") || s.Contains("mute"))),
    ("ドアを開けないでください。", "ja", "en", s => s.Contains("door") && (s.Contains("not") || s.Contains("don't")))
};
var provider = new GoogleFreeTranslateProvider();
int passed = 0;
foreach (var item in cases)
{
    string output = await provider.TranslateAsync(item.Source, item.To, item.From);
    bool success = item.Check(output.ToLowerInvariant());
    if (success) passed++;
    Console.WriteLine($"{(success ? "PASS" : "FAIL")} {item.From}->{item.To}: {item.Source} => {output}");
}
Console.WriteLine($"Meaning smoke checks: {passed}/{cases.Length}. This is not a general accuracy score or ASR benchmark.");
Environment.ExitCode = passed == cases.Length ? 0 : 1;
