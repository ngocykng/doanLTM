using System.Speech.Synthesis;

namespace EnglishFlashcard3D.Services;

public class AudioService
{
    private readonly SpeechSynthesizer _synthesizer = new();

    public AudioService()
    {
        _synthesizer.Rate = 0;
        _synthesizer.Volume = 100;
    }

    public void SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _synthesizer.SpeakAsyncCancelAll();
        _synthesizer.SpeakAsync(text);
    }
}
