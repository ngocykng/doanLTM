using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using BilingualVocabApi.Models;
using MySqlConnector;

namespace BilingualVocabApi.Services;

/// <summary>Persists the API's existing domain snapshot in the MySQL tables used by mysql_workbench.sql.</summary>
public sealed class MySqlPersistence
{
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };

    public MySqlPersistence(IConfiguration configuration, IWebHostEnvironment env)
    {
        LoadDotEnv(Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", ".env")));
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Environment.GetEnvironmentVariable("DB_HOST") ?? configuration["DB_HOST"] ?? "127.0.0.1",
            Port = uint.TryParse(Environment.GetEnvironmentVariable("DB_PORT") ?? configuration["DB_PORT"], out var port) ? port : 3306,
            UserID = Environment.GetEnvironmentVariable("DB_USER") ?? configuration["DB_USER"] ?? "root",
            Password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? configuration["DB_PASSWORD"] ?? string.Empty,
            Database = Environment.GetEnvironmentVariable("DB_NAME") ?? configuration["DB_NAME"] ?? "bilingual_vocab_dau",
            CharacterSet = "utf8mb4",
            SslMode = MySqlSslMode.None,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 4,
            AllowUserVariables = true
        };
        _connectionString = builder.ConnectionString;
    }

    private static void LoadDotEnv(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadLines(path))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#') || !text.Contains('=')) continue;
            var index = text.IndexOf('=');
            var key = text[..index].Trim();
            var value = text[(index + 1)..].Trim().Trim('"', '\'');
            if ((key.StartsWith("DB_", StringComparison.OrdinalIgnoreCase) || key.Equals("GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase)) && Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    public bool TryLoad(out DatabaseSchema? data, out string? error)
    {
        data = null; error = null;
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            using (var checkTopicOwner = new MySqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='topics' AND COLUMN_NAME='owner_user_id'", connection))
            {
                if (Convert.ToInt32(checkTopicOwner.ExecuteScalar()) == 0)
                    using (var addTopicOwner = new MySqlCommand("ALTER TABLE topics ADD COLUMN owner_user_id VARCHAR(64) NULL", connection))
                        addTopicOwner.ExecuteNonQuery();
            }
            var loaded = new DatabaseSchema
            {
                Users = ReadRows<User>(connection, "users"),
                Topics = ReadRows<Topic>(connection, "topics"),
                Vocab_Cards = ReadRows<VocabCard>(connection, "vocab_cards"),
                Video_Words = ReadRows<VideoWord>(connection, "video_words"),
                Assignments = ReadRows<Assignment>(connection, "assignments"),
                Submissions = ReadRows<Submission>(connection, "submissions"),
                Api_Logs = ReadRows<ApiLog>(connection, "api_logs")
            };
            loaded.Video_Sessions = ReadRows<VideoSession>(connection, "video_sessions", row =>
            {
                var subtitles = row["subtitles_json"]?.ToString();
                row.Remove("subtitles_json");
                if (!string.IsNullOrWhiteSpace(subtitles)) row["subtitles"] = JsonNode.Parse(subtitles);
            });
            loaded.Consultations = ReadRows<Consultation>(connection, "consultations", row =>
            {
                foreach (var (column, property) in new[] { ("strengths_json", "strengths"), ("weaknesses_json", "weaknesses") })
                {
                    var json = row[column]?.ToString(); row.Remove(column);
                    if (!string.IsNullOrWhiteSpace(json)) row[property] = JsonNode.Parse(json);
                }
            });
            var ai = ReadRows<AiConfig>(connection, "ai_config").FirstOrDefault();
            if (ai is not null) loaded.Ai_Config = ai;
            data = loaded;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public void Save(DatabaseSchema data)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            Replace(connection, transaction, "users", data.Users);
            Replace(connection, transaction, "topics", data.Topics);
            Replace(connection, transaction, "vocab_cards", data.Vocab_Cards);
            Replace(connection, transaction, "video_sessions", data.Video_Sessions);
            Replace(connection, transaction, "video_words", data.Video_Words);
            Replace(connection, transaction, "assignments", data.Assignments);
            Replace(connection, transaction, "submissions", data.Submissions);
            Replace(connection, transaction, "consultations", data.Consultations);
            Replace(connection, transaction, "api_logs", data.Api_Logs);
            Replace(connection, transaction, "ai_config", new[] { data.Ai_Config });
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    private List<T> ReadRows<T>(MySqlConnection connection, string table, Action<Dictionary<string, object?>>? transform = null)
    {
        using var command = new MySqlCommand($"SELECT * FROM `{table}`", connection);
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            transform?.Invoke(row);
            var json = JsonSerializer.Serialize(row);
            var value = JsonSerializer.Deserialize<T>(json, _json);
            if (value is not null) result.Add(value);
        }
        return result;
    }

    private static void Replace<T>(MySqlConnection connection, MySqlTransaction transaction, string table, IEnumerable<T> values)
    {
        using (var clear = new MySqlCommand($"DELETE FROM `{table}`", connection, transaction)) clear.ExecuteNonQuery();
        var rows = values.ToList();
        if (rows.Count == 0) return;
        foreach (var item in rows)
        {
            var columns = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in item!.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.Name is "Card_Count" or "Submission_Count") continue;
                var column = ToSnakeCase(property.Name);
                var value = property.GetValue(item);
                if (table == "video_sessions" && property.Name == "Subtitles") { column = "subtitles_json"; value = JsonSerializer.Serialize(value); }
                if (table == "consultations" && property.Name == "Strengths") { column = "strengths_json"; value = JsonSerializer.Serialize(value); }
                if (table == "consultations" && property.Name == "Weaknesses") { column = "weaknesses_json"; value = JsonSerializer.Serialize(value); }
                columns[column] = value;
            }
            if (table == "ai_config") columns["id"] = 1;
            var names = columns.Keys.ToList();
            var parameters = names.Select((_, i) => $"@p{i}").ToList();
            using var insert = new MySqlCommand($"INSERT INTO `{table}` ({string.Join(",", names.Select(n => $"`{n}`"))}) VALUES ({string.Join(",", parameters)})", connection, transaction);
            for (var i = 0; i < names.Count; i++) insert.Parameters.AddWithValue(parameters[i], columns[names[i]] ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }
    }

    private static string ToSnakeCase(string value)
    {
        var result = new System.Text.StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c) && i > 0 && value[i - 1] != '_') result.Append('_');
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}
