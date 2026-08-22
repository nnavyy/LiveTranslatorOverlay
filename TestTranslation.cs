using System;
using System.Threading.Tasks;
using LiveTranslatorOverlay.Core.Translation;

class Program
{
    static async Task Main(string[] args)
    {
        var provider = new GroqTranslateProvider("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");
        Console.WriteLine("Translating...");
        string result = await provider.TranslateAsync("Hello world", "id", "en");
        Console.WriteLine("Result: " + result);
    }
}
