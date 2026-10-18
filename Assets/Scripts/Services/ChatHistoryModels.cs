using System;
using System.Collections.Generic;

[Serializable]
public sealed class ChatMessageRecord
{
    public string id;
    public string role;
    public string content;
    public string status;
    public string createdAtUtc;
    public string updatedAtUtc;

    public bool IsPromptEligible(bool includeInterruptedAssistant)
    {
        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        if (role == "system")
        {
            return status == "completed";
        }

        if (role == "user")
        {
            return status != "deleted";
        }

        if (role != "assistant")
        {
            return false;
        }

        if (status == "completed")
        {
            return true;
        }

        return includeInterruptedAssistant && status == "interrupted";
    }
}

[Serializable]
public sealed class ChatSessionRecord
{
    public string id;
    public string title;
    public string createdAtUtc;
    public string updatedAtUtc;
    public List<ChatMessageRecord> messages = new List<ChatMessageRecord>();
}

public sealed class ChatSessionSummary
{
    public string id;
    public string title;
    public string updatedAtUtc;
    public string filePath;
    public int messageCount;
}

public sealed class ConversationLine
{
    public string role;
    public string text;
    public string status;
}
