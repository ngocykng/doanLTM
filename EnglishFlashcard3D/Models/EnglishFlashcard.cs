namespace EnglishFlashcard3D.Models;

public class EnglishFlashcard
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Word { get; set; } = string.Empty;
    public string Phonetic { get; set; } = string.Empty;
    public string PartOfSpeech { get; set; } = string.Empty;
    public string MeaningVi { get; set; } = string.Empty;
    public string ExampleEn { get; set; } = string.Empty;
    public string ExampleVi { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int ReviewCount { get; set; }
    public bool IsMastered { get; set; }
    public DateTime? LastReviewedAt { get; set; }
}
