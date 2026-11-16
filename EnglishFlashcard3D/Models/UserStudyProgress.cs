namespace EnglishFlashcard3D.Models;

public class UserStudyProgress
{
    public int TotalCards { get; set; }
    public int MasteredCards { get; set; }
    public int ReviewedCards { get; set; }
    public DateTime LastStudiedAt { get; set; } = DateTime.Now;
}
