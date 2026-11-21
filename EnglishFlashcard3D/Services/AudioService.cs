using System.Speech.Synthesis;

namespace EnglishFlashcard3D.Services;

public class AudioService : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();

    public AudioService()
    {
        try
        {
            var englishVoice = _synthesizer.GetInstalledVoices()
                .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("en-", StringComparison.OrdinalIgnoreCase));

            if (englishVoice != null)
            {
                _synthesizer.SelectVoice(englishVoice.VoiceInfo.Name);
            }

            _synthesizer.Rate = -1; // Tốc độ ~0.9 chuẩn học ngoại ngữ
            _synthesizer.Volume = 100;
        }
        catch
        {
            // Fallback default voice
        }
    }

    public void SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            _synthesizer.SpeakAsyncCancelAll();
            _synthesizer.SpeakAsync(text);
        }
        catch
        {
            // Ignore synthesis error on devices without sound hardware
        }
    }

    public void Dispose()
    {
        _synthesizer.Dispose();
    }
}
