using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

public sealed class ChatHistoryStore
{
    private const string FileExtension = ".json";
    private readonly string rootDirectory;

    public ChatHistoryStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Chat history root directory is empty.", nameof(rootDirectory));
        }

        this.rootDirectory = rootDirectory;
    }

    public string RootDirectory => rootDirectory;

    public void EnsureRootDirectory()
    {
        Directory.CreateDirectory(rootDirectory);
    }

    public ChatSessionRecord LoadLatestOrCreate()
    {
        var latest = ListSessions().OrderByDescending(summary => summary.updatedAtUtc).FirstOrDefault();
        return latest != null ? LoadSession(latest.id) : CreateSession();
    }

    public ChatSessionRecord CreateSession(string title = null)
    {
        EnsureRootDirectory();
        var now = NowIso();
        var session = new ChatSessionRecord
        {
            id = AllocateTimestampSessionId(DateTime.Now),
            title = string.IsNullOrWhiteSpace(title) ? "新对话" : title.Trim(),
            createdAtUtc = now,
            updatedAtUtc = now,
            messages = new List<ChatMessageRecord>()
        };

        SaveSession(session);
        return session;
    }

    public ChatSessionRecord LoadSession(string sessionId)
    {
        EnsureRootDirectory();
        var path = GetSessionPath(sessionId);
        if (!File.Exists(path))
        {
            return CreateSession();
        }

        var json = File.ReadAllText(path);
        var session = JsonConvert.DeserializeObject<ChatSessionRecord>(json);
        if (session == null)
        {
            return CreateSession();
        }

        if (session.messages == null)
        {
            session.messages = new List<ChatMessageRecord>();
        }

        NormalizeLoadedSession(session);
        return session;
    }

    public List<ChatSessionSummary> ListSessions()
    {
        EnsureRootDirectory();
        var summaries = new List<ChatSessionSummary>();
        foreach (var file in Directory.GetFiles(rootDirectory, "*" + FileExtension, SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(file);
                var session = JsonConvert.DeserializeObject<ChatSessionRecord>(json);
                if (session == null || string.IsNullOrWhiteSpace(session.id))
                {
                    continue;
                }

                summaries.Add(new ChatSessionSummary
                {
                    id = session.id,
                    title = string.IsNullOrWhiteSpace(session.title) ? "未命名对话" : session.title,
                    updatedAtUtc = session.updatedAtUtc,
                    filePath = file,
                    messageCount = session.messages != null ? session.messages.Count : 0
                });
            }
            catch
            {
                // Ignore unreadable session files so one bad file does not block chat startup.
            }
        }

        return summaries.OrderByDescending(summary => summary.updatedAtUtc).ToList();
    }

    public int DeleteAllSessions()
    {
        EnsureRootDirectory();
        var deleted = 0;
        foreach (var file in Directory.GetFiles(rootDirectory, "*" + FileExtension, SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
            deleted++;
        }

        foreach (var tempFile in Directory.GetFiles(rootDirectory, "*" + FileExtension + ".tmp", SearchOption.TopDirectoryOnly))
        {
            File.Delete(tempFile);
        }

        return deleted;
    }

    public void SaveSession(ChatSessionRecord session)
    {
        if (session == null)
        {
            return;
        }

        EnsureRootDirectory();
        var previousId = session.id;
        var previousPath = string.IsNullOrWhiteSpace(previousId) ? null : GetSessionPath(previousId);

        if (string.IsNullOrWhiteSpace(session.id))
        {
            session.id = AllocateTimestampSessionId(GetSessionLocalCreatedTime(session));
        }
        else if (ShouldMigrateSessionIdToTimestamp(session.id))
        {
            session.id = AllocateTimestampSessionId(GetSessionLocalCreatedTime(session));
        }

        if (string.IsNullOrWhiteSpace(session.createdAtUtc))
        {
            session.createdAtUtc = NowIso();
        }

        session.updatedAtUtc = NowIso();
        if (session.messages == null)
        {
            session.messages = new List<ChatMessageRecord>();
        }

        UpdateTitleFromFirstUserMessage(session);

        var path = GetSessionPath(session.id);
        var tempPath = path + ".tmp";
        var json = JsonConvert.SerializeObject(session, Formatting.Indented);
        File.WriteAllText(tempPath, json);

        if (File.Exists(path))
        {
            File.Copy(tempPath, path, true);
            File.Delete(tempPath);
        }
        else
        {
            File.Move(tempPath, path);
        }

        if (!string.IsNullOrWhiteSpace(previousPath)
            && !string.Equals(previousPath, path, StringComparison.OrdinalIgnoreCase)
            && File.Exists(previousPath))
        {
            File.Delete(previousPath);
        }
    }

    public ChatMessageRecord AppendMessage(ChatSessionRecord session, string role, string content, string status)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        if (session.messages == null)
        {
            session.messages = new List<ChatMessageRecord>();
        }

        var now = NowIso();
        var message = new ChatMessageRecord
        {
            id = Guid.NewGuid().ToString("N"),
            role = role,
            content = content ?? string.Empty,
            status = string.IsNullOrWhiteSpace(status) ? "completed" : status,
            createdAtUtc = now,
            updatedAtUtc = now
        };

        session.messages.Add(message);
        SaveSession(session);
        return message;
    }

    public void UpdateMessage(ChatSessionRecord session, ChatMessageRecord message, string content, string status)
    {
        if (session == null || message == null)
        {
            return;
        }

        message.content = content ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(status))
        {
            message.status = status;
        }

        message.updatedAtUtc = NowIso();
        SaveSession(session);
    }

    public string GetSessionPath(string sessionId)
    {
        var safeId = SanitizeFileName(string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId);
        return Path.Combine(rootDirectory, safeId + FileExtension);
    }

    public static List<ChatMessageRecord> FindRelevantMessages(
        IReadOnlyList<ChatMessageRecord> messages,
        string query,
        int maxMessages,
        int maxCharacters,
        int excludeLastCount,
        bool includeInterruptedAssistant)
    {
        var result = new List<ChatMessageRecord>();
        if (messages == null || messages.Count == 0 || maxMessages <= 0 || maxCharacters <= 0)
        {
            return result;
        }

        var tokens = TextSearchUtility.ExtractTokens(query);
        if (tokens.Count == 0)
        {
            return result;
        }

        var lastIndexExclusive = Math.Max(0, messages.Count - Math.Max(0, excludeLastCount));
        var scored = new List<Tuple<int, float>>();
        for (var i = 0; i < lastIndexExclusive; i++)
        {
            var message = messages[i];
            if (message == null
                || message.role == "system"
                || !message.IsPromptEligible(includeInterruptedAssistant))
            {
                continue;
            }

            var score = TextSearchUtility.ScoreText(TextSearchUtility.NormalizeForSearch(message.content), tokens);
            if (score > 0f)
            {
                scored.Add(Tuple.Create(i, score));
            }
        }

        var selectedIndexes = new SortedSet<int>();
        foreach (var item in scored.OrderByDescending(item => item.Item2).ThenByDescending(item => item.Item1))
        {
            if (selectedIndexes.Count >= maxMessages)
            {
                break;
            }

            selectedIndexes.Add(item.Item1);

            var message = messages[item.Item1];
            if (message.role == "user" && item.Item1 + 1 < lastIndexExclusive)
            {
                var next = messages[item.Item1 + 1];
                if (next != null && next.role == "assistant" && next.IsPromptEligible(includeInterruptedAssistant))
                {
                    selectedIndexes.Add(item.Item1 + 1);
                }
            }
            else if (message.role == "assistant" && item.Item1 > 0)
            {
                var previous = messages[item.Item1 - 1];
                if (previous != null && previous.role == "user" && previous.IsPromptEligible(includeInterruptedAssistant))
                {
                    selectedIndexes.Add(item.Item1 - 1);
                }
            }
        }

        var usedCharacters = 0;
        foreach (var index in selectedIndexes)
        {
            if (index < 0 || index >= messages.Count)
            {
                continue;
            }

            var message = messages[index];
            if (message == null || string.IsNullOrWhiteSpace(message.content))
            {
                continue;
            }

            var contentLength = message.content.Length;
            if (usedCharacters + contentLength > maxCharacters)
            {
                break;
            }

            result.Add(message);
            usedCharacters += contentLength;
        }

        return result;
    }

    private static void UpdateTitleFromFirstUserMessage(ChatSessionRecord session)
    {
        if (session == null || session.messages == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.title) && session.title != "新对话")
        {
            return;
        }

        foreach (var message in session.messages)
        {
            if (message != null && message.role == "user" && !string.IsNullOrWhiteSpace(message.content))
            {
                var compact = TextSearchUtility.CompactWhitespace(message.content);
                session.title = compact.Length > 24 ? compact.Substring(0, 24) + "..." : compact;
                return;
            }
        }
    }

    private static void NormalizeLoadedSession(ChatSessionRecord session)
    {
        if (session == null || session.messages == null)
        {
            return;
        }

        foreach (var message in session.messages)
        {
            if (message == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(message.status))
            {
                message.status = "completed";
            }

            if (message.status == "streaming")
            {
                message.status = "interrupted";
            }
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        return fileName;
    }

    private string AllocateTimestampSessionId(DateTime localTime)
    {
        EnsureRootDirectory();
        var baseId = localTime.ToString("yyyy-MM-dd_HH-mm");
        if (!File.Exists(GetSessionPath(baseId)))
        {
            return baseId;
        }

        for (var i = 2; i < 1000; i++)
        {
            var candidate = baseId + "-" + i.ToString("00");
            if (!File.Exists(GetSessionPath(candidate)))
            {
                return candidate;
            }
        }

        return baseId + "-" + Guid.NewGuid().ToString("N");
    }

    private static bool ShouldMigrateSessionIdToTimestamp(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return true;
        }

        if (sessionId.Length >= 16
            && DateTime.TryParseExact(
                sessionId.Substring(0, 16),
                "yyyy-MM-dd_HH-mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out _))
        {
            return false;
        }

        return Guid.TryParseExact(sessionId, "N", out _);
    }

    private static DateTime GetSessionLocalCreatedTime(ChatSessionRecord session)
    {
        if (session != null
            && !string.IsNullOrWhiteSpace(session.createdAtUtc)
            && DateTime.TryParse(
                session.createdAtUtc,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var createdAt))
        {
            return createdAt.ToLocalTime();
        }

        return DateTime.Now;
    }

    private static string NowIso()
    {
        return DateTime.UtcNow.ToString("o");
    }
}
