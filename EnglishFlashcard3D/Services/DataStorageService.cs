using System.IO;
using System.Text.Json;
using EnglishFlashcard3D.Models;

namespace EnglishFlashcard3D.Services;

public class DataStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    public List<EnglishFlashcard> LoadDefaultCards()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var filePath = Path.Combine(baseDirectory, "Assets", "Data", "default_cards.json");

        if (!File.Exists(filePath))
        {
            return new List<EnglishFlashcard>();
        }

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<EnglishFlashcard>>(json, JsonOptions) ?? new List<EnglishFlashcard>();
    }
}
