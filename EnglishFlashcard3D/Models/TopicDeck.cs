using System.Windows;

namespace EnglishFlashcard3D.Models;

public class TopicDeck
{
    private static readonly (string fill, string shadow)[] NodePalette =
    {
        ("#58CC02", "#58A700"),
        ("#1CB0F6", "#1899D6"),
        ("#CE82FF", "#A560E8"),
        ("#FF9600", "#CC7800")
    };

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Level { get; set; } = "B1";
    public int CardCount { get; set; }
    public List<EnglishFlashcard> Cards { get; set; } = new();

    public int Index { get; set; }
    public string CardCountText => $"{CardCount} thẻ";
    public string UnitLabel => $"UNIT {Index + 1}";

    public string NodeColor => NodePalette[Math.Abs(Index) % NodePalette.Length].fill;
    public string NodeShadowColor => NodePalette[Math.Abs(Index) % NodePalette.Length].shadow;

    /// <summary>Độ lệch ngang tạo đường lộ trình zic-zac kiểu Duolingo.</summary>
    public Thickness PathOffsetMargin
    {
        get
        {
            double[] wave = { 0, 72, 120, 72, 0, -72, -120, -72 };
            double offset = wave[Math.Abs(Index) % wave.Length];
            return offset >= 0
                ? new Thickness(offset, 10, 0, 10)
                : new Thickness(0, 10, -offset, 10);
        }
    }
}
