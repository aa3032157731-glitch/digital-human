using System;
using System.Text;

// Deterministic chunker; timestamps are supplied by the caller for repeatable tests.
public sealed class TtsTextChunker
{
    private readonly StringBuilder pending = new StringBuilder();
    private readonly AzureTTSSettings settings;
    private long pendingSince;
    private bool first = true;
    public TtsTextChunker(AzureTTSSettings settings) { this.settings = settings; }
    public bool IsEmpty => pending.Length == 0;
    public void Append(string text, long nowMs)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (pending.Length == 0) pendingSince = nowMs;
        pending.Append(text);
        if (pending.Length > 65536) throw new InvalidOperationException("待合成文本过长，本轮朗读已停止。");
    }
    public string Take(long nowMs, bool complete)
    {
        if (pending.Length == 0) return null;
        int cap = Math.Min(settings.maxChunkChars, pending.Length);
        int count = 0;
        for (int i = 0; i < cap; i++)
        {
            char c = pending[i];
            if ("。！？；!?;\n".IndexOf(c) >= 0 || (c == '.' && (i + 1 == pending.Length || char.IsWhiteSpace(pending[i + 1])))) { count = i + 1; break; }
        }
        if (count == 0)
        {
            int target = first ? settings.firstChunkChars : settings.nextChunkChars;
            int wait = first ? settings.firstWaitMs : settings.nextWaitMs;
            if (!complete && pending.Length < target && nowMs - pendingSince < wait) return null;
            count = Math.Min(cap, target);
            if (complete || nowMs - pendingSince >= wait) count = cap;
            // Prefer a word boundary near the target. Never split a surrogate pair.
            if (count < pending.Length && IsWord(pending[count - 1]) && IsWord(pending[count]))
            {
                int boundary = count;
                while (boundary > 0 && IsWord(pending[boundary - 1])) boundary--;
                if (boundary > 0) count = boundary;
                else
                {
                    while (count < cap && IsWord(pending[count])) count++;
                    if (!complete && count == pending.Length && count < settings.maxChunkChars && nowMs - pendingSince < wait) return null;
                }
            }
        }
        if (count < pending.Length && char.IsHighSurrogate(pending[count - 1]) && char.IsLowSurrogate(pending[count])) count--;
        if (count == pending.Length && char.IsHighSurrogate(pending[count - 1]) && !complete) count--;
        if (count <= 0) return null;
        string result = pending.ToString(0, count);
        pending.Remove(0, count);
        if (!string.IsNullOrWhiteSpace(result)) first = false;
        pendingSince = nowMs;
        return result;
    }
    private static bool IsWord(char c) => c < 128 && (char.IsLetterOrDigit(c) || c == '\'' || c == '-');
}
