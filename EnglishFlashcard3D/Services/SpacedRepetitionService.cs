using EnglishFlashcard3D.Models;

namespace EnglishFlashcard3D.Services;

/// <summary>Thuật toán lặp lại ngắt quãng SM-2 tối giản cho Flashcard 3D.</summary>
public class SpacedRepetitionService
{
    /// <summary>Cập nhật lịch ôn tập tiếp theo dựa trên mức độ ghi nhớ (quality: 0-5).</summary>
    public void ApplyReview(EnglishFlashcard card, int quality)
    {
        quality = Math.Clamp(quality, 0, 5);
        var now = DateTime.UtcNow;

        card.ReviewCount += 1;
        card.LastReviewedAt = now;

        if (quality < 3)
        {
            card.IntervalDays = 1;
            card.IsMastered = false;
        }
        else
        {
            card.IntervalDays = card.ReviewCount switch
            {
                1 => 1,
                2 => 6,
                _ => Math.Max(1, (int)Math.Round(Math.Max(1, card.IntervalDays) * card.EaseFactor))
            };
            card.IsMastered = true;
        }

        double ef = card.EaseFactor + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02));
        card.EaseFactor = Math.Round(Math.Max(1.3, ef), 2);
        card.NextReviewAt = now.AddDays(card.IntervalDays);
    }
}
