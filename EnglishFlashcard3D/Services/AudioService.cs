using System.Speech.Synthesis;
using System.Windows.Media;

namespace EnglishFlashcard3D.Services;

public class AudioService : IDisposable
{
    private readonly SpeechSynthesizer _synthesizer = new();
    private MediaPlayer? _mediaPlayer;

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

    public void StopSpeaking()
    {
        try { _synthesizer.SpeakAsyncCancelAll(); }
        catch { }
    }

    public bool PlayMedia(string? urlOrPath, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(urlOrPath))
        {
            error = "Chưa có liên kết audio.";
            return false;
        }
        if (!Uri.TryCreate(urlOrPath.Trim(), UriKind.Absolute, out var uri))
        {
            error = "Đường dẫn audio không hợp lệ.";
            return false;
        }
        try
        {
            _mediaPlayer ??= new MediaPlayer();
            _mediaPlayer.Stop();
            _mediaPlayer.Open(uri);
            _mediaPlayer.Volume = 1.0;
            _mediaPlayer.Play();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void StopMedia()
    {
        try { _mediaPlayer?.Stop(); }
        catch { }
        StopSpeaking();
    }

    public void Dispose()
    {
        StopMedia();
        _mediaPlayer?.Close();
        _synthesizer.Dispose();
    }
}
