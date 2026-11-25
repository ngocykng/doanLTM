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
    private readonly string _serverConnectionString;
    private readonly string _databaseName;
    private readonly string _contentRootPath;
    private readonly bool _isTesting;
    private readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };

    public MySqlPersistence(IConfiguration configuration, IWebHostEnvironment env)
    {
        _isTesting = env.IsEnvironment("Testing") || string.Equals(Environment.GetEnvironmentVariable("DISABLE_MYSQL"), "true", StringComparison.OrdinalIgnoreCase);
        _contentRootPath = env.ContentRootPath;
        LoadDotEnv(env.ContentRootPath);
        LoadDotEnv(AppContext.BaseDirectory);
        LoadDotEnv(Directory.GetCurrentDirectory());
        var rawDbName = Environment.GetEnvironmentVariable("DB_NAME") ?? configuration["DB_NAME"] ?? "bilingual_vocab_dau";
        _databaseName = System.Text.RegularExpressions.Regex.IsMatch(rawDbName, "^[A-Za-z0-9_]+$") ? rawDbName : "bilingual_vocab_dau";
        var builder = new MySqlConnectionStringBuilder
        {
            Server = Environment.GetEnvironmentVariable("DB_HOST") ?? configuration["DB_HOST"] ?? "localhost",
            Port = uint.TryParse(Environment.GetEnvironmentVariable("DB_PORT") ?? configuration["DB_PORT"], out var port) ? port : 3306,
            UserID = Environment.GetEnvironmentVariable("DB_USER") ?? configuration["DB_USER"] ?? "root",
            Password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? configuration["DB_PASSWORD"] ?? string.Empty,
            Database = _databaseName,
            CharacterSet = "utf8mb4",
            SslMode = MySqlSslMode.None,
            AllowPublicKeyRetrieval = true,
            ConnectionTimeout = 4,
            AllowUserVariables = true
        };
        _connectionString = builder.ConnectionString;
        builder.Database = string.Empty;
        _serverConnectionString = builder.ConnectionString;
    }

    private static void LoadDotEnv(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        for (int depth = 0; depth < 8 && dir is not null; depth++, dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path)) continue;
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
            break;
        }
    }

    public void EnsureDatabaseAndTables()
    {
        if (_isTesting) return;

        using (var serverConnection = new MySqlConnection(_serverConnectionString))
        {
            serverConnection.Open();
            using var createDb = new MySqlCommand(
                $"CREATE DATABASE IF NOT EXISTS `{_databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;",
                serverConnection);
            createDb.ExecuteNonQuery();
        }

        using var dbConnection = new MySqlConnection(_connectionString);
        dbConnection.Open();
        foreach (var statement in LoadTableCreationStatements(_contentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory()))
        {
            using var cmd = new MySqlCommand(statement, dbConnection);
            cmd.ExecuteNonQuery();
        }
    }

    public static List<string> LoadTableCreationStatements(params string[] searchDirectories)
    {
        string? schemaText = null;
        foreach (var dir in searchDirectories.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(dir, "mysql_schema.sql");
            if (File.Exists(candidate))
            {
                schemaText = File.ReadAllText(candidate);
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(schemaText))
            return new List<string>();

        return schemaText
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(stmt => stmt.StartsWith("CREATE TABLE", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public bool TryLoad(out DatabaseSchema? data, out string? error)
    {
        data = null; error = null;
        if (_isTesting) { error = "Testing environment uses isolated JSON storage."; return false; }
        try
        {
            EnsureDatabaseAndTables();
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
                Api_Logs = ReadRows<ApiLog>(connection, "api_logs")
            };
            loaded.Assignments = ReadRows<Assignment>(connection, "assignments", row =>
            {
                DeserializeJsonColumn(row, "topic_ids_json", "topic_ids");
                DeserializeJsonColumn(row, "topic_names_json", "topic_names");
                DeserializeJsonColumn(row, "exam_structure_json", "exam_structure");
            });
            loaded.Submissions = ReadRows<Submission>(connection, "submissions", row =>
            {
                DeserializeJsonColumn(row, "exam_answers_json", "exam_answers");
                DeserializeJsonColumn(row, "section_scores_json", "section_scores");
                DeserializeJsonColumn(row, "exam_review_json", "exam_review");
            });
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

    public bool TrySync(DatabaseSchema data, out string message)
    {
        try
        {
            EnsureDatabaseAndTables();
            Save(data);
            message = $"Đã đồng bộ {data.Users.Count} người dùng, {data.Topics.Count} chủ đề, {data.Vocab_Cards.Count} thẻ và {data.Assignments.Count} bài tập sang MySQL.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Không thể đồng bộ MySQL: {ex.Message}";
            return false;
        }
    }

    public void Save(DatabaseSchema data) => SaveTables(
        data,
        "users", "topics", "vocab_cards", "video_sessions", "video_words",
        "assignments", "submissions", "consultations", "api_logs", "ai_config");

    public void SaveTables(DatabaseSchema data, params string[] tableNames)
    {
        if (_isTesting || tableNames.Length == 0) return;
        var targets = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            if (targets.Contains("users")) Replace(connection, transaction, "users", data.Users);
            if (targets.Contains("topics")) Replace(connection, transaction, "topics", data.Topics);
            if (targets.Contains("vocab_cards")) Replace(connection, transaction, "vocab_cards", data.Vocab_Cards);
            if (targets.Contains("video_sessions")) Replace(connection, transaction, "video_sessions", data.Video_Sessions);
            if (targets.Contains("video_words")) Replace(connection, transaction, "video_words", data.Video_Words);
            if (targets.Contains("assignments")) Replace(connection, transaction, "assignments", data.Assignments);
            if (targets.Contains("submissions")) Replace(connection, transaction, "submissions", data.Submissions);
            if (targets.Contains("consultations")) Replace(connection, transaction, "consultations", data.Consultations);
            if (targets.Contains("api_logs")) Replace(connection, transaction, "api_logs", data.Api_Logs);
            if (targets.Contains("ai_config")) Replace(connection, transaction, "ai_config", new[] { data.Ai_Config });
            transaction.Commit();
        }
        catch { transaction.Rollback(); throw; }
    }

    public void InsertApiLog(ApiLog log)
    {
        if (_isTesting) return;
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            var columns = BuildRowColumns("api_logs", log);
            var names = columns.Keys.ToList();
            var parameters = names.Select((_, i) => $"@p{i}").ToList();
            using var insert = new MySqlCommand(
                $"INSERT IGNORE INTO `api_logs` ({string.Join(",", names.Select(n => $"`{n}`"))}) VALUES ({string.Join(",", parameters)})",
                connection);
            for (var i = 0; i < names.Count; i++)
                insert.Parameters.AddWithValue(parameters[i], columns[names[i]] ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }
        catch { }
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
            var columns = BuildRowColumns(table, item!);
            var names = columns.Keys.ToList();
            var parameters = names.Select((_, i) => $"@p{i}").ToList();
            using var insert = new MySqlCommand($"INSERT INTO `{table}` ({string.Join(",", names.Select(n => $"`{n}`"))}) VALUES ({string.Join(",", parameters)})", connection, transaction);
            for (var i = 0; i < names.Count; i++) insert.Parameters.AddWithValue(parameters[i], columns[names[i]] ?? DBNull.Value);
            insert.ExecuteNonQuery();
        }
    }

    private static Dictionary<string, object?> BuildRowColumns<T>(string table, T item)
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
            if (table == "assignments" && property.Name == "Topic_Ids") { column = "topic_ids_json"; value = JsonSerializer.Serialize(value); }
            if (table == "assignments" && property.Name == "Topic_Names") { column = "topic_names_json"; value = JsonSerializer.Serialize(value); }
            if (table == "assignments" && property.Name == "Exam_Structure") { column = "exam_structure_json"; value = JsonSerializer.Serialize(value); }
            if (table == "submissions" && property.Name == "Exam_Answers") { column = "exam_answers_json"; value = JsonSerializer.Serialize(value); }
            if (table == "submissions" && property.Name == "Section_Scores") { column = "section_scores_json"; value = JsonSerializer.Serialize(value); }
            if (table == "submissions" && property.Name == "Exam_Review") { column = "exam_review_json"; value = JsonSerializer.Serialize(value); }
            columns[column] = value;
        }
        if (table == "ai_config") columns["id"] = 1;
        return columns;
    }

    private static void DeserializeJsonColumn(Dictionary<string, object?> row, string jsonCol, string propName)
    {
        if (!row.TryGetValue(jsonCol, out var raw)) return;
        var text = raw?.ToString();
        row.Remove(jsonCol);
        if (!string.IsNullOrWhiteSpace(text))
            row[propName] = JsonNode.Parse(text);
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
