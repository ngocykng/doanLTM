namespace BilingualVocabApi.Models;

public class DatabaseSchema
{
    public List<Topic> Topics { get; set; } = new();
    public List<VocabCard> Vocab_Cards { get; set; } = new();
    public List<VideoSession> Video_Sessions { get; set; } = new();
    public List<VideoWord> Video_Words { get; set; } = new();
    public List<User> Users { get; set; } = new();
    public List<Assignment> Assignments { get; set; } = new();
    public List<Submission> Submissions { get; set; } = new();
    public List<Consultation> Consultations { get; set; } = new();
    public AiConfig Ai_Config { get; set; } = new();
    public List<ApiLog> Api_Logs { get; set; } = new();
}

